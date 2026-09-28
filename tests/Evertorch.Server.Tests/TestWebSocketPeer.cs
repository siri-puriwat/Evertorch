using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client end of one WebSocket connection to <see cref="WebSocketServerTransport" />, over a loopback TCP pair,
///     or over a real <c>wss://</c> connection. A background reader collects every message the server sends and, unless
///     told not to, answers its heartbeat pings as a client does.
/// </summary>
internal sealed class TestWebSocketPeer : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<byte[]> m_received = new();
    private readonly CancellationTokenSource m_stop = new();
    private readonly SemaphoreSlim m_sending = new(1, 1);
    private readonly Task m_reader;
    private bool m_isDisposed;

    /// <param name="isReading">False for a peer that stays connected but never reads what the server sends.</param>
    public TestWebSocketPeer(WebSocket socket, bool isAnsweringPings = true, bool isReading = true)
    {
        Socket = socket;
        IsAnsweringPings = isAnsweringPings;
        m_reader = isReading ? Task.Run(ReadAsync) : Task.CompletedTask;
    }

    public WebSocket Socket { get; }

    public bool IsAnsweringPings { get; set; }

    /// <summary>
    ///     Set once the server's close arrived or the connection failed.
    /// </summary>
    public ManualResetEventSlim Closed { get; } = new();

    public WebSocketCloseStatus? CloseStatus { get; private set; }

    public void Dispose()
    {
        if (m_isDisposed)
        {
            return;
        }

        m_isDisposed = true;
        m_stop.Cancel();
        Socket.Abort();
        try
        {
            m_reader.Wait(Timeout);
        }
        catch (AggregateException)
        {
            // The reader ends with the socket.
        }

        Socket.Dispose();
        m_stop.Dispose();
    }

    /// <summary>
    ///     The server end of a loopback pair, and this peer as its client.
    /// </summary>
    public static (WebSocket Server, TestWebSocketPeer Client) CreatePair(
        bool isAnsweringPings = true,
        bool isReading = true)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        TcpClient accepted = listener.AcceptTcpClient();
        listener.Stop();
        var server = WebSocket.CreateFromStream(
            accepted.GetStream(),
            new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
        var peer = WebSocket.CreateFromStream(
            client.GetStream(),
            new WebSocketCreationOptions { IsServer = false, KeepAliveInterval = TimeSpan.Zero });
        return (server, new TestWebSocketPeer(peer, isAnsweringPings, isReading));
    }

    public static byte[] Frame(byte channel, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[payload.Length + 1];
        frame[0] = channel;
        payload.CopyTo(frame.AsSpan(1));
        return frame;
    }

    public static byte[] Heartbeat(byte kind, uint nonce)
    {
        byte[] frame = new byte[WebSocketServerTransport.HeartbeatLength];
        frame[0] = WebSocketServerTransport.HeartbeatChannel;
        frame[1] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2), nonce);
        return frame;
    }

    public void Send(ReadOnlySpan<byte> message, WebSocketMessageType type = WebSocketMessageType.Binary)
    {
        byte[] copy = message.ToArray();
        m_sending.Wait();
        try
        {
            Socket.SendAsync(copy, type, true, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            m_sending.Release();
        }
    }

    public void Send(ProtocolChannel channel, ReadOnlySpan<byte> payload)
    {
        Send(Frame((byte)channel, payload));
    }

    /// <summary>
    ///     Sends one binary message in <paramref name="parts" />, each part a frame of its own.
    /// </summary>
    public void SendFragments(params byte[][] parts)
    {
        m_sending.Wait();
        try
        {
            for (int index = 0; index < parts.Length; index++)
            {
                Socket.SendAsync(parts[index], WebSocketMessageType.Binary, index == parts.Length - 1,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        finally
        {
            m_sending.Release();
        }
    }

    /// <summary>
    ///     The next message the server sent that is not a heartbeat, or null when none came in time.
    /// </summary>
    public byte[]? Receive(TimeSpan? limit = null)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan wait = limit ?? Timeout;
        while (elapsed.Elapsed < wait)
        {
            if (m_received.TryTake(out byte[]? message, wait - elapsed.Elapsed)
                && message[0] != WebSocketServerTransport.HeartbeatChannel)
            {
                return message;
            }
        }

        return null;
    }

    /// <summary>
    ///     The next message of any kind, heartbeats included.
    /// </summary>
    public byte[]? ReceiveAny(TimeSpan? limit = null)
    {
        return m_received.TryTake(out byte[]? message, limit ?? Timeout) ? message : null;
    }

    public bool WaitForClose(TimeSpan? limit = null)
    {
        return Closed.Wait(limit ?? Timeout);
    }

    private async Task ReadAsync()
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (!m_stop.IsCancellationRequested)
            {
                int length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await Socket.ReceiveAsync(buffer.AsMemory(length), m_stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        CloseStatus = Socket.CloseStatus;
                        return;
                    }

                    length += result.Count;
                } while (!result.EndOfMessage);

                byte[] message = buffer.AsSpan(0, length).ToArray();
                if (IsAnsweringPings
                    && message.Length == WebSocketServerTransport.HeartbeatLength
                    && message[0] == WebSocketServerTransport.HeartbeatChannel
                    && message[1] == WebSocketServerTransport.HeartbeatPing)
                {
                    uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(2));
                    await m_sending.WaitAsync();
                    try
                    {
                        await Socket.SendAsync(
                            Heartbeat(WebSocketServerTransport.HeartbeatPong, nonce),
                            WebSocketMessageType.Binary,
                            true,
                            m_stop.Token);
                    }
                    finally
                    {
                        m_sending.Release();
                    }
                }

                m_received.Add(message);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                                              or ObjectDisposedException)
        {
            // Closed without a close message.
        }
        finally
        {
            Closed.Set();
        }
    }
}
}
