using System;
using System.Runtime.InteropServices;

namespace Evertorch.Client
{
/// <summary>
///     A browser's WebSocket through the page's bridge (<c>Plugins/WebGL/EvertorchWebSocket.jslib</c>), for web builds,
///     where .NET's sockets do not run. It compiles on every platform, but only a web build may create one: elsewhere
///     the bridge's functions do not exist. Polled: the page queues what arrives, and nothing calls back into C#.
/// </summary>
public sealed class BrowserWebSocketConnection : IWebSocketConnection
{
    private readonly byte[] m_buffer = new byte[WebSocketClientTransport.MaxMessageBytes + 1];
    private int m_socket;

    public WebSocketConnectionState State =>
        m_socket == 0 ? WebSocketConnectionState.Connecting : (WebSocketConnectionState)EvertorchSocketState(m_socket);

    public void Open(string url)
    {
        m_socket = EvertorchSocketOpen(url);
    }

    public void Send(byte[] message)
    {
        if (m_socket != 0)
        {
            EvertorchSocketSend(m_socket, message, message.Length);
        }
    }

    // A message longer than the buffer arrives as one of the limit plus one byte, which the transport refuses.
    public bool TryReceive(out byte[] message)
    {
        message = Array.Empty<byte>();
        if (m_socket == 0)
        {
            return false;
        }

        int length = EvertorchSocketReceive(m_socket, m_buffer, m_buffer.Length);
        if (length == -1)
        {
            return false;
        }

        message = length < 0 ? new byte[m_buffer.Length] : m_buffer.AsSpan(0, length).ToArray();
        return true;
    }

    public void Close()
    {
        if (m_socket != 0)
        {
            EvertorchSocketClose(m_socket);
        }
    }

    public void Dispose()
    {
        if (m_socket != 0)
        {
            EvertorchSocketFree(m_socket);
            m_socket = 0;
        }
    }

    [DllImport("__Internal")]
    private static extern int EvertorchSocketOpen(string url);

    [DllImport("__Internal")]
    private static extern int EvertorchSocketState(int socket);

    [DllImport("__Internal")]
    private static extern int EvertorchSocketSend(int socket, byte[] message, int length);

    /// <returns>The message's length; -1 when none is waiting; -2 when it was longer than the buffer.</returns>
    [DllImport("__Internal")]
    private static extern int EvertorchSocketReceive(int socket, byte[] buffer, int capacity);

    [DllImport("__Internal")]
    private static extern void EvertorchSocketClose(int socket);

    [DllImport("__Internal")]
    private static extern void EvertorchSocketFree(int socket);
}
}
