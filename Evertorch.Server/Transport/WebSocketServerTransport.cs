using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Browsers' connections over WebSocket, beside the UDP clients (Network Protocol §7). The gateway checks each
///     request and hands the accepted socket over; from then on this adapter, like the LiteNetLib one, only decodes into
///     <see cref="InboundQueue" /> and keeps its peer table. One binary message is one channel byte and one payload;
///     channel 255 is this transport's heartbeat. Each connection has one writer and a bounded send queue, so a send
///     never blocks the tick thread.
/// </summary>
public sealed class WebSocketServerTransport : IServerTransport
{
    public const byte HeartbeatChannel = 255;
    public const byte HeartbeatPing = 1;
    public const byte HeartbeatPong = 2;
    public const int HeartbeatLength = 6;
    public const int MaxHeartbeatsPerSecond = 4;

    /// <summary>
    ///     The largest payload either end accepts: what one UDP datagram carries (<see cref="NpcServices.MaxEncodedLength" />
    ///     ).
    ///     Larger than any client message today, so a hello of a later version still reaches <see cref="InboundQueue" />'s
    ///     version check (Network Protocol §5) instead of being cut off here.
    /// </summary>
    public const int MaxPayloadBytes = NpcServices.MaxEncodedLength;

    public const int MaxMessageBytes = 1 + MaxPayloadBytes;

    public const int MaxQueuedMessages = 4096;
    public const int MaxQueuedBytes = 1024 * 1024;

    private static readonly TimeSpan StopAllowance = TimeSpan.FromSeconds(1);

    private static readonly Action<ILogger, long, int, int, Exception?> LogSendQueueFull =
        LoggerMessage.Define<long, int, int>(
            LogLevel.Warning,
            new EventId(3003, "WebSocketSendQueueFull"),
            "Connection {Connection} was closed: its send queue held {Messages} messages and {Bytes} bytes.");

    private readonly InboundQueue m_inbound;
    private readonly NetworkOptions m_options;
    private readonly AddressThrottle m_throttle;
    private readonly ConnectionRegistry m_connections;
    private readonly ServerInstruments m_instruments;
    private readonly AuditLog m_audit;
    private readonly ILogger<WebSocketServerTransport> m_logger;
    private readonly TimeSpan m_heartbeatInterval;
    private readonly TimeSpan m_silenceLimit;
    private readonly ConcurrentDictionary<ConnectionId, Peer> m_peers = new();

    // As in the LiteNetLib adapter: the session layer may ask for a cooldown after a peer left on its own.
    private readonly ConcurrentDictionary<ConnectionId, IPAddress> m_departed = new();
    private readonly ConcurrentQueue<ConnectionId> m_departures = new();

    private long m_bytesReceived;
    private long m_bytesSent;
    private long m_messagesReceived;
    private long m_messagesSent;
    private volatile bool m_isStarted;
    private volatile bool m_isStopped;
    private volatile bool m_isAdmissionClosed;

    public WebSocketServerTransport(
        InboundQueue inbound,
        IOptions<NetworkOptions> options,
        AddressThrottle throttle,
        ConnectionRegistry connections,
        ServerInstruments instruments,
        AuditLog audit,
        ILogger<WebSocketServerTransport> logger)
    {
        m_inbound = inbound;
        m_options = options.Value;
        m_throttle = throttle;
        m_connections = connections;
        m_instruments = instruments;
        m_audit = audit;
        m_logger = logger;
        m_silenceLimit = TimeSpan.FromMilliseconds(m_options.DisconnectTimeoutMs);
        m_heartbeatInterval = TimeSpan.FromMilliseconds(Math.Min(1000, m_options.DisconnectTimeoutMs / 4));
    }

    public int ConnectionCount => m_peers.Count;

    public bool IsAdmissionOpen => !m_isAdmissionClosed;

    public void Start()
    {
        m_isStarted = true;
    }

    public void CloseAdmission()
    {
        m_isAdmissionClosed = true;
    }

    // The notices are queued at once; every socket still open a second later is aborted, well within the lifetime
    // service's allowance after the drain (System Architecture §13).
    public void Stop(DisconnectReason reason, string message)
    {
        CloseAdmission();
        m_isStopped = true;
        byte[] notice = EncodeNotice(reason, message);
        var closing = new List<Peer>();
        foreach (Peer peer in m_peers.Values)
        {
            peer.CloseByServer(notice);
            closing.Add(peer);
        }

        var waited = Stopwatch.StartNew();
        foreach (Peer peer in closing)
        {
            TimeSpan left = StopAllowance - waited.Elapsed;
            if (left <= TimeSpan.Zero || !peer.Finished.Wait(left))
            {
                peer.Abort();
            }
        }
    }

    public void Send(ConnectionId connection, ReadOnlySpan<byte> payload)
    {
        if (!m_peers.TryGetValue(connection, out Peer? peer))
        {
            return;
        }

        if (!MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery _))
        {
            throw new ArgumentException("The payload does not start with a routable opcode.", nameof(payload));
        }

        byte[] frame = new byte[payload.Length + 1];
        frame[0] = (byte)channel;
        payload.CopyTo(frame.AsSpan(1));
        peer.Enqueue(frame);
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason, string message)
    {
        if (m_peers.TryGetValue(connection, out Peer? peer))
        {
            peer.CloseByServer(EncodeNotice(reason, message));
        }
    }

    public void CoolDownAddress(ConnectionId connection)
    {
        if (m_peers.TryGetValue(connection, out Peer? peer))
        {
            m_throttle.StartCooldown(peer.Address);
        }
        else if (m_departed.TryRemove(connection, out IPAddress? address))
        {
            m_throttle.StartCooldown(address);
        }
    }

    public TransportStatistics GetStatistics()
    {
        return new TransportStatistics(
            Interlocked.Read(ref m_bytesReceived),
            Interlocked.Read(ref m_bytesSent),
            Interlocked.Read(ref m_messagesReceived),
            Interlocked.Read(ref m_messagesSent),
            0);
    }

    public bool TryGetRoundTripTime(ConnectionId connection, out int milliseconds)
    {
        milliseconds = 0;
        return m_peers.TryGetValue(connection, out Peer? peer) && peer.TryGetRoundTripTime(out milliseconds);
    }

    /// <summary>
    ///     What the gateway answers a request for <c>/play</c> before any upgrade (Network Protocol §7): 0 to upgrade,
    ///     else the HTTP status. Safe on any thread.
    /// </summary>
    public int CheckRequest(IPAddress remote, string? key)
    {
        if (!m_isStarted || m_isStopped)
        {
            return 503;
        }

        if (!m_throttle.TryAdmit(Normalize(remote), out string limit))
        {
            Refused(limit);
            return 429;
        }

        return string.Equals(key, m_options.ConnectionKey, StringComparison.Ordinal) ? 0 : 403;
    }

    /// <summary>
    ///     Runs an upgraded connection until it closes. A cooling address, a closed admission, and a full server each
    ///     get their notice as the one message before the close.
    /// </summary>
    public async Task RunAsync(WebSocket socket, IPAddress remote, CancellationToken aborted)
    {
        IPAddress address = Normalize(remote);
        DisconnectReason refusal = DisconnectReason.None;
        ConnectionId connection = default;
        if (m_throttle.IsCoolingDown(address))
        {
            Refused(ServerInstruments.AddressCooldownLimit);
            refusal = DisconnectReason.RateLimited;
        }
        else if (m_isAdmissionClosed)
        {
            refusal = DisconnectReason.Maintenance;
        }
        else if (!m_connections.TryAdmit(this, out connection))
        {
            refusal = DisconnectReason.ServerFull;
        }

        if (refusal != DisconnectReason.None)
        {
            await RefuseAsync(socket, refusal, aborted).ConfigureAwait(false);
            return;
        }

        var peer = new Peer(this, connection, socket, address);
        m_peers[connection] = peer;
        m_throttle.OnConnected(address);
        m_inbound.OnConnected(connection);
        try
        {
            await peer.RunAsync(aborted).ConfigureAwait(false);
        }
        finally
        {
            Forget(peer);
        }
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static byte[] EncodeNotice(DisconnectReason reason, string message)
    {
        var notice = new DisconnectNotice(reason, message);
        byte[] payload = new byte[notice.GetEncodedLength() + 1];
        payload[0] = (byte)ProtocolChannel.Control;
        notice.Write(payload.AsSpan(1));
        return payload;
    }

    private static async Task RefuseAsync(WebSocket socket, DisconnectReason reason, CancellationToken aborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timeout.CancelAfter(StopAllowance);
        try
        {
            await socket.SendAsync(EncodeNotice(reason, string.Empty), WebSocketMessageType.Binary, true, timeout.Token)
                .ConfigureAwait(false);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            socket.Abort();
        }
    }

    private void Refused(string limit)
    {
        m_instruments.RecordRateLimited(limit);
        m_audit.ConnectionRefused(default, null, limit);
    }

    private void Forget(Peer peer)
    {
        m_peers.TryRemove(peer.Connection, out Peer? _);
        if (!peer.IsClosedByServer)
        {
            m_departed[peer.Connection] = peer.Address;
            m_departures.Enqueue(peer.Connection);
            while (m_departures.Count > m_options.MaxConnections && m_departures.TryDequeue(out ConnectionId oldest))
            {
                m_departed.TryRemove(oldest, out IPAddress? _);
            }
        }

        m_throttle.OnDisconnected(peer.Address);

        // The session layer already removed a connection it closed itself; only the queue's budget is left to forget.
        if (peer.IsClosedByServer)
        {
            m_inbound.OnClosed(peer.Connection);
        }
        else
        {
            m_inbound.OnDisconnected(peer.Connection);
        }

        m_connections.Release(peer.Connection);
    }

    private sealed class Peer
    {
        private readonly WebSocketServerTransport m_transport;
        private readonly WebSocket m_socket;

        private readonly Channel<byte[]> m_queue = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = true });

        private readonly Stopwatch m_clock = Stopwatch.StartNew();
        private readonly object m_gate = new();
        private int m_queuedMessages;
        private int m_queuedBytes;
        private bool m_isClosing;
        private volatile bool m_isClosedByServer;
        private long m_lastReceivedTicks;
        private uint m_pingNonce;
        private uint m_answeredNonce;
        private long m_pingSentTicks;
        private int m_roundTripMilliseconds = -1;
        private long m_heartbeatWindowStart;
        private int m_heartbeatsInWindow;

        public Peer(WebSocketServerTransport transport, ConnectionId connection, WebSocket socket, IPAddress address)
        {
            m_transport = transport;
            Connection = connection;
            m_socket = socket;
            Address = address;
        }

        public ConnectionId Connection { get; }

        public IPAddress Address { get; }

        public bool IsClosedByServer => m_isClosedByServer;

        public ManualResetEventSlim Finished { get; } = new();

        public async Task RunAsync(CancellationToken aborted)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted);
            Interlocked.Exchange(ref m_lastReceivedTicks, m_clock.Elapsed.Ticks);
            Task writer = WriteAsync(stop.Token);
            Task heartbeat = BeatAsync(stop.Token);
            try
            {
                await ReceiveAsync(stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // The socket failed or was aborted; the connection is gone either way.
            }
            finally
            {
                m_queue.Writer.TryComplete();
                stop.CancelAfter(StopAllowance);
                await Task.WhenAll(IgnoreFailureAsync(writer), IgnoreFailureAsync(heartbeat)).ConfigureAwait(false);
                stop.Cancel();
                Finished.Set();
            }
        }

        public void Enqueue(byte[] frame)
        {
            bool isOverflowing;
            lock (m_gate)
            {
                if (m_isClosing)
                {
                    return;
                }

                isOverflowing = m_queuedMessages + 1 > MaxQueuedMessages ||
                    m_queuedBytes + frame.Length > MaxQueuedBytes;
                if (!isOverflowing)
                {
                    m_queuedMessages++;
                    m_queuedBytes += frame.Length;
                    m_queue.Writer.TryWrite(frame);
                }
            }

            if (isOverflowing)
            {
                LogSendQueueFull(m_transport.m_logger, Connection.Value, m_queuedMessages, m_queuedBytes, null);
                Abort();
            }
        }

        // The notice is the last message; the writer closes the socket once it is out.
        public void CloseByServer(byte[] notice)
        {
            lock (m_gate)
            {
                if (m_isClosing)
                {
                    return;
                }

                m_isClosedByServer = true;
                m_isClosing = true;
                m_queue.Writer.TryWrite(notice);
                m_queue.Writer.TryComplete();
            }

            _ = AbortUnlessFinishedAsync();
        }

        // A peer that never answers the close would otherwise hold its socket until the silence limit.
        private async Task AbortUnlessFinishedAsync()
        {
            await Task.Delay(StopAllowance).ConfigureAwait(false);
            if (!Finished.IsSet)
            {
                Abort();
            }
        }

        public void Abort()
        {
            lock (m_gate)
            {
                m_isClosing = true;
                m_queue.Writer.TryComplete();
            }

            m_socket.Abort();
        }

        public bool TryGetRoundTripTime(out int milliseconds)
        {
            milliseconds = Volatile.Read(ref m_roundTripMilliseconds);
            return milliseconds >= 0;
        }

        private static async Task IgnoreFailureAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                                  or ObjectDisposedException)
            {
                // The connection is closing; what the loop was doing no longer matters.
            }
        }

        private async Task ReceiveAsync(CancellationToken stop)
        {
            byte[] buffer = new byte[MaxMessageBytes + 1];
            while (true)
            {
                int length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (length == buffer.Length)
                    {
                        Violate(WebSocketCloseStatus.MessageTooBig);
                        return;
                    }

                    result = await m_socket.ReceiveAsync(buffer.AsMemory(length), stop).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        Violate(WebSocketCloseStatus.InvalidMessageType);
                        return;
                    }

                    length += result.Count;
                } while (!result.EndOfMessage);

                if (length > MaxMessageBytes || length == 0 || !Handle(buffer.AsSpan(0, length)))
                {
                    Violate(length > MaxMessageBytes
                        ? WebSocketCloseStatus.MessageTooBig
                        : WebSocketCloseStatus.PolicyViolation);
                    return;
                }
            }
        }

        // False when the message breaks the framing, which closes the connection.
        private bool Handle(ReadOnlySpan<byte> message)
        {
            Interlocked.Exchange(ref m_lastReceivedTicks, m_clock.Elapsed.Ticks);
            Interlocked.Add(ref m_transport.m_bytesReceived, message.Length);
            Interlocked.Increment(ref m_transport.m_messagesReceived);
            byte channel = message[0];
            if (channel == HeartbeatChannel)
            {
                return HandleHeartbeat(message);
            }

            if (channel >= MessageRouting.ChannelCount)
            {
                return false;
            }

            // A connection the server closed has no session and no budget left; nothing it sends may reach the queue.
            if (!m_isClosedByServer)
            {
                MessageDelivery delivery = channel == (byte)ProtocolChannel.Control
                    ? MessageDelivery.ReliableOrdered
                    : MessageDelivery.UnreliableSequenced;
                m_transport.m_inbound.OnPayload(Connection, (ProtocolChannel)channel, delivery, message.Slice(1));
            }

            return true;
        }

        // The first answer to the server's latest ping is outside the budget: the server sets that pace, which at a short
        // silence limit is itself four pings a second (Network Protocol §7). The receive loop alone reads the nonce.
        private bool HandleHeartbeat(ReadOnlySpan<byte> message)
        {
            if (message.Length != HeartbeatLength)
            {
                return false;
            }

            uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(2));
            bool isAnswer = message[1] == HeartbeatPong
                && nonce == Volatile.Read(ref m_pingNonce)
                && nonce != m_answeredNonce;
            if (!isAnswer && !IsWithinHeartbeatBudget())
            {
                return false;
            }

            switch (message[1])
            {
                case HeartbeatPing:
                    Enqueue(Heartbeat(HeartbeatPong, nonce));
                    return true;
                case HeartbeatPong:
                    if (isAnswer)
                    {
                        m_answeredNonce = nonce;
                        long sent = Interlocked.Read(ref m_pingSentTicks);
                        int elapsed = (int)TimeSpan.FromTicks(m_clock.Elapsed.Ticks - sent).TotalMilliseconds;
                        Volatile.Write(ref m_roundTripMilliseconds, Math.Max(0, elapsed));
                    }

                    return true;
                default:
                    return false;
            }
        }

        // Only the receive loop calls this, so the window needs no lock.
        private bool IsWithinHeartbeatBudget()
        {
            long now = m_clock.ElapsedMilliseconds;
            if (now - m_heartbeatWindowStart >= 1000)
            {
                m_heartbeatWindowStart = now;
                m_heartbeatsInWindow = 0;
            }

            return ++m_heartbeatsInWindow <= MaxHeartbeatsPerSecond;
        }

        private void Violate(WebSocketCloseStatus status)
        {
            lock (m_gate)
            {
                m_isClosing = true;
                m_queue.Writer.TryComplete();
            }

            _ = CloseQuietlyAsync(status);
        }

        private async Task CloseQuietlyAsync(WebSocketCloseStatus status)
        {
            using var timeout = new CancellationTokenSource(StopAllowance);
            try
            {
                await m_socket.CloseOutputAsync(status, string.Empty, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                                  or ObjectDisposedException)
            {
                m_socket.Abort();
            }
        }

        private async Task WriteAsync(CancellationToken stop)
        {
            while (await m_queue.Reader.WaitToReadAsync(stop).ConfigureAwait(false))
            {
                while (m_queue.Reader.TryRead(out byte[]? frame))
                {
                    lock (m_gate)
                    {
                        m_queuedMessages = Math.Max(0, m_queuedMessages - 1);
                        m_queuedBytes = Math.Max(0, m_queuedBytes - frame.Length);
                    }

                    await m_socket.SendAsync(frame, WebSocketMessageType.Binary, true, stop).ConfigureAwait(false);
                    Interlocked.Add(ref m_transport.m_bytesSent, frame.Length);
                    Interlocked.Increment(ref m_transport.m_messagesSent);
                }
            }

            if (m_isClosedByServer && m_socket.State == WebSocketState.Open)
            {
                await m_socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, stop)
                    .ConfigureAwait(false);
            }
        }

        private async Task BeatAsync(CancellationToken stop)
        {
            using var timer = new PeriodicTimer(m_transport.m_heartbeatInterval);
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                long silent = m_clock.Elapsed.Ticks - Interlocked.Read(ref m_lastReceivedTicks);
                if (TimeSpan.FromTicks(silent) >= m_transport.m_silenceLimit)
                {
                    Abort();
                    return;
                }

                uint nonce = unchecked(Volatile.Read(ref m_pingNonce) + 1);
                Interlocked.Exchange(ref m_pingSentTicks, m_clock.Elapsed.Ticks);
                Volatile.Write(ref m_pingNonce, nonce);
                Enqueue(Heartbeat(HeartbeatPing, nonce));
            }
        }

        private static byte[] Heartbeat(byte kind, uint nonce)
        {
            byte[] frame = new byte[HeartbeatLength];
            frame[0] = HeartbeatChannel;
            frame[1] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), nonce);
            return frame;
        }
    }
}
}
