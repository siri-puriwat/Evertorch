namespace Evertorch.Server
{
/// <summary>
/// Read-only counters a transport keeps about the network. Safe to call from the tick thread.
/// </summary>
public interface ITransportStatistics
{
    TransportStatistics GetStatistics();

    /// <summary>
    /// False when the connection is unknown or no round trip has been measured yet.
    /// </summary>
    bool TryGetRoundTripTime(ConnectionId connection, out int milliseconds);
}
}
