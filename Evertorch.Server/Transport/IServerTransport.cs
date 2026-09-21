namespace Evertorch.Server
{
/// <summary>
/// The network edge of the server. Implementations decode what arrives into the <see cref="InboundQueue"/> and
/// never touch sessions or the world themselves.
/// </summary>
public interface IServerTransport : IOutboundMessages, ITransportStatistics
{
    /// <summary>
    /// The port actually bound, which differs from the configured one when that is 0.
    /// </summary>
    int LocalPort { get; }

    /// <summary>
    /// Binds the socket and begins accepting connections. Throws when the endpoint cannot be bound.
    /// </summary>
    void Start();

    /// <summary>
    /// From now on new connections are refused with <see cref="Evertorch.Protocol.DisconnectReason.Maintenance"/>.
    /// Existing connections are unaffected.
    /// </summary>
    void CloseAdmission();

    /// <summary>
    /// Tells every peer the server is going away, closes them, and releases the socket.
    /// </summary>
    void Stop();
}
}
