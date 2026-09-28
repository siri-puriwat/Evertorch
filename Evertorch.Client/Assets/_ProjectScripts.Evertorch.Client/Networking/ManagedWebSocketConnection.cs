using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Evertorch.Client
{
/// <summary>
///     A WebSocket through .NET's <see cref="ClientWebSocket" />, for the editor, desktop players, and tests (a browser
///     build uses its page's bridge instead). A receive loop and one send loop run off the calling thread; everything
///     reaches the caller through queues it drains when it polls.
/// </summary>
public sealed class ManagedWebSocketConnection : IWebSocketConnection
{
    private readonly ClientWebSocket m_socket = new();
    private readonly ConcurrentQueue<byte[]> m_received = new();
    private readonly ConcurrentQueue<byte[]> m_outbox = new();
    private readonly SemaphoreSlim m_outboxSignal = new(0);
    private readonly CancellationTokenSource m_stop = new();
    private int m_state = (int)WebSocketConnectionState.Connecting;

    /// <param name="configure">
    ///     Changes the socket's options before it connects, such as the certificate check a test uses to pin its
    ///     server's certificate.
    /// </param>
    public ManagedWebSocketConnection(Action<ClientWebSocketOptions>? configure = null)
    {
        // The transport keeps its own heartbeat (Network Protocol §7).
        m_socket.Options.KeepAliveInterval = TimeSpan.Zero;
        configure?.Invoke(m_socket.Options);
    }

    public WebSocketConnectionState State => (WebSocketConnectionState)Volatile.Read(ref m_state);

    public void Open(string url)
    {
        _ = RunAsync(new Uri(url));
    }

    public void Send(byte[] message)
    {
        if (State == WebSocketConnectionState.Open)
        {
            m_outbox.Enqueue(message);
            m_outboxSignal.Release();
        }
    }

    public bool TryReceive(out byte[] message)
    {
        return m_received.TryDequeue(out message!);
    }

    public void Close()
    {
        if (State == WebSocketConnectionState.Open)
        {
            m_outbox.Enqueue(Array.Empty<byte>());
            m_outboxSignal.Release();
        }
        else
        {
            m_stop.Cancel();
        }
    }

    public void Dispose()
    {
        m_stop.Cancel();
        m_socket.Abort();
        m_socket.Dispose();
    }

    private static bool IsSocketFailure(Exception exception)
    {
        return exception is WebSocketException
            or OperationCanceledException
            or ObjectDisposedException
            or InvalidOperationException
            or IOException;
    }

    private async Task RunAsync(Uri url)
    {
        try
        {
            await m_socket.ConnectAsync(url, m_stop.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsSocketFailure(exception))
        {
            Volatile.Write(ref m_state, (int)WebSocketConnectionState.Failed);
            return;
        }

        Volatile.Write(ref m_state, (int)WebSocketConnectionState.Open);
        Task sending = SendAllAsync();
        try
        {
            await ReceiveAllAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsSocketFailure(exception))
        {
            // The socket closed without a close handshake; the state below says so.
        }
        finally
        {
            Volatile.Write(ref m_state, (int)WebSocketConnectionState.Closed);
            m_stop.Cancel();
        }

        try
        {
            await sending.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsSocketFailure(exception))
        {
            // The send loop ends with the socket.
        }
    }

    // A message longer than the limit arrives as one of the limit plus one byte, which the transport refuses.
    private async Task ReceiveAllAsync()
    {
        byte[] buffer = new byte[WebSocketClientTransport.MaxMessageBytes + 1];
        while (true)
        {
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length)
                {
                    m_received.Enqueue(buffer.AsSpan(0, length).ToArray());
                    return;
                }

                result = await m_socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length),
                        m_stop.Token)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                length += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                m_received.Enqueue(buffer.AsSpan(0, length).ToArray());
            }
        }
    }

    // An empty message in the outbox is the close, sent after everything queued before it.
    private async Task SendAllAsync()
    {
        while (true)
        {
            await m_outboxSignal.WaitAsync(m_stop.Token).ConfigureAwait(false);
            if (!m_outbox.TryDequeue(out byte[]? message))
            {
                continue;
            }

            if (message.Length == 0)
            {
                await m_socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_stop.Token)
                    .ConfigureAwait(false);
                return;
            }

            await m_socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, m_stop.Token)
                .ConfigureAwait(false);
        }
    }
}
}
