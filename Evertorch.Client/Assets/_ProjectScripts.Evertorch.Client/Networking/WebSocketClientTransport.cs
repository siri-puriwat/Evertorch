using System;
using System.Buffers.Binary;
using System.Diagnostics;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's side of the WebSocket game connection (Network Protocol §7): one binary message is one channel byte
///     and one payload, channel 255 is the transport's heartbeat, and the server's <c>DisconnectNotice</c> arrives as
///     the last message before its close. Everything happens in <see cref="Poll" />, on the calling thread; the
///     connection underneath is the browser's or a managed one.
/// </summary>
public sealed class WebSocketClientTransport : IClientTransport
{
    public const string PlayPath = "/play";
    public const byte HeartbeatChannel = 255;
    public const byte HeartbeatPing = 1;
    public const byte HeartbeatPong = 2;
    public const int HeartbeatLength = 6;

    /// <summary>
    ///     The longest message either end accepts: a channel byte and what one UDP datagram carries.
    /// </summary>
    public const int MaxMessageBytes = 1 + 1020;

    // Only the round trip needs the client's own pings; its answers to the server's keep the server's silence limit
    // fed, so one ping a second stays well inside the server's heartbeat budget at any silence limit.
    private const double PingIntervalSeconds = 1.0;

    private readonly Func<IWebSocketConnection> m_createConnection;
    private readonly string m_connectionKey;
    private readonly double m_silenceSeconds;
    private readonly Func<double> m_clock;
    private IWebSocketConnection? m_connection;
    private byte[] m_notice = Array.Empty<byte>();
    private bool m_isOpenReported;
    private bool m_isClosedLocally;
    private bool m_isTimedOut;
    private bool m_isBroken;
    private double m_lastReceived;
    private double m_lastPing;
    private uint m_pingNonce;
    private double m_pingSentAt;

    /// <param name="createConnection">A new, unopened connection for each <see cref="Connect" />.</param>
    /// <param name="clock">Seconds from any fixed start; a stopwatch when not given.</param>
    public WebSocketClientTransport(
        Func<IWebSocketConnection> createConnection,
        string connectionKey,
        int disconnectTimeoutMilliseconds,
        Func<double>? clock = null)
    {
        m_createConnection = createConnection ?? throw new ArgumentNullException(nameof(createConnection));
        m_connectionKey = connectionKey ?? throw new ArgumentNullException(nameof(connectionKey));
        m_silenceSeconds = disconnectTimeoutMilliseconds / 1000.0;
        if (clock == null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }

        m_clock = clock;
    }

    public bool IsConnected => m_connection != null && m_isOpenReported && !m_isBroken;

    public int RoundTripMilliseconds { get; private set; }

    public void Connect(string host, int port)
    {
        m_connection?.Dispose();
        m_notice = Array.Empty<byte>();
        m_isOpenReported = false;
        m_isClosedLocally = false;
        m_isTimedOut = false;
        m_isBroken = false;
        RoundTripMilliseconds = 0;
        m_lastReceived = m_clock();
        m_lastPing = m_lastReceived;
        m_connection = m_createConnection();
        m_connection.Open(PlayUrl(host, port, m_connectionKey));
    }

    public void Disconnect()
    {
        if (m_connection != null)
        {
            m_isClosedLocally = true;
            m_connection.Close();
        }
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        if (!IsConnected)
        {
            return;
        }

        byte[] message = new byte[payload.Length + 1];
        message[0] = (byte)channel;
        payload.CopyTo(message.AsSpan(1));
        m_connection!.Send(message);
    }

    public void Poll(IClientTransportListener listener)
    {
        if (listener == null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        IWebSocketConnection? connection = m_connection;
        if (connection == null)
        {
            return;
        }

        if (!m_isOpenReported && connection.State == WebSocketConnectionState.Open)
        {
            m_isOpenReported = true;
            m_lastReceived = m_clock();
            listener.OnConnected();
        }

        while (!m_isBroken && connection.TryReceive(out byte[] message))
        {
            Receive(message, listener);
        }

        double now = m_clock();
        if (m_isOpenReported && !m_isBroken && connection.State == WebSocketConnectionState.Open)
        {
            if (now - m_lastReceived >= m_silenceSeconds)
            {
                m_isTimedOut = true;
                m_isBroken = true;
                connection.Close();
            }
            else if (now - m_lastPing >= PingIntervalSeconds)
            {
                m_lastPing = now;
                m_pingNonce++;
                m_pingSentAt = now;
                connection.Send(Heartbeat(HeartbeatPing, m_pingNonce));
            }
        }

        if (m_isBroken
            || connection.State == WebSocketConnectionState.Closed
            || connection.State == WebSocketConnectionState.Failed)
        {
            ReportClosed(connection, listener);
        }
    }

    public void Dispose()
    {
        m_connection?.Dispose();
        m_connection = null;
    }

    /// <summary>
    ///     The URL of the game connection on the gateway at <paramref name="host" /> and <paramref name="port" />.
    /// </summary>
    public static string PlayUrl(string host, int port, string connectionKey)
    {
        string bracketed = host.IndexOf(':') >= 0 ? $"[{host}]" : host;
        return $"wss://{bracketed}:{port}{PlayPath}?key={Uri.EscapeDataString(connectionKey)}";
    }

    private static byte[] Heartbeat(byte kind, uint nonce)
    {
        byte[] message = new byte[HeartbeatLength];
        message[0] = HeartbeatChannel;
        message[1] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(2), nonce);
        return message;
    }

    private void Receive(byte[] message, IClientTransportListener listener)
    {
        m_lastReceived = m_clock();
        if (message.Length == 0 || message.Length > MaxMessageBytes)
        {
            Break();
            return;
        }

        byte channel = message[0];
        if (channel == HeartbeatChannel)
        {
            ReceiveHeartbeat(message);
            return;
        }

        if (channel >= MessageRouting.ChannelCount)
        {
            Break();
            return;
        }

        ReadOnlySpan<byte> payload = message.AsSpan(1);

        // The server's last word before its close; the listener hears it with the disconnect, as over UDP.
        if (MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode) && opcode == MessageOpcode.DisconnectNotice)
        {
            m_notice = payload.ToArray();
            return;
        }

        listener.OnPayload((ProtocolChannel)channel, payload);
    }

    private void ReceiveHeartbeat(byte[] message)
    {
        if (message.Length != HeartbeatLength)
        {
            Break();
            return;
        }

        uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(2));
        if (message[1] == HeartbeatPing)
        {
            m_connection?.Send(Heartbeat(HeartbeatPong, nonce));
        }
        else if (message[1] == HeartbeatPong && nonce == m_pingNonce)
        {
            RoundTripMilliseconds = (int)Math.Max(0, Math.Round((m_clock() - m_pingSentAt) * 1000));
        }
    }

    // A server that breaks the framing is not one this client can talk to.
    private void Break()
    {
        m_isBroken = true;
        m_connection?.Close();
    }

    private void ReportClosed(IWebSocketConnection connection, IClientTransportListener listener)
    {
        TransportDisconnectCause cause = m_isClosedLocally ? TransportDisconnectCause.ClosedLocally
            : m_isTimedOut ? TransportDisconnectCause.TimedOut
            : !m_isOpenReported ? TransportDisconnectCause.ConnectionFailed
            : m_isBroken ? TransportDisconnectCause.Other
            : TransportDisconnectCause.ClosedByServer;
        byte[] notice = m_notice;
        connection.Dispose();
        m_connection = null;
        m_notice = Array.Empty<byte>();
        listener.OnDisconnected(cause, notice);
    }
}
}
