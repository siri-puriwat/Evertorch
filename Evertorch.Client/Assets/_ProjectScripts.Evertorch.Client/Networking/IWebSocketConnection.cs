using System;

namespace Evertorch.Client
{
public enum WebSocketConnectionState
{
    Connecting = 0,
    Open = 1,
    Closed = 2,

    /// <summary>
    ///     Closed before it ever opened: the server could not be reached, refused the upgrade, or was not trusted.
    /// </summary>
    Failed = 3
}

/// <summary>
///     One WebSocket, polled from the client's frame loop: a browser's through the page's bridge, or a managed one.
///     Only binary messages are ever sent or received.
/// </summary>
public interface IWebSocketConnection : IDisposable
{
    WebSocketConnectionState State { get; }

    void Open(string url);

    /// <summary>
    ///     Queues one binary message; ignored unless the socket is open.
    /// </summary>
    void Send(byte[] message);

    bool TryReceive(out byte[] message);

    void Close();
}
}
