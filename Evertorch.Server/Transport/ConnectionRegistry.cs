using System.Threading;

namespace Evertorch.Server
{
/// <summary>
///     The one source of connection IDs for every transport (System Architecture §8): from 1 and never reused,
///     whichever transport accepted the connection, so two transports never hand the session layer the same ID. Safe
///     on any thread.
/// </summary>
public sealed class ConnectionRegistry
{
    private long m_last;

    public ConnectionId Allocate()
    {
        return new ConnectionId(Interlocked.Increment(ref m_last));
    }
}
}
