using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Every transport's connections in one table (System Architecture §8): the IDs, from 1 and never reused, so two
///     transports never hand the session layer the same one; which transport owns each, so a send reaches it; and
///     <c>Network:MaxConnections</c> across all of them. A slot is taken when a transport accepts a connection and
///     given back when it reports the connection gone, so a closed peer still counts until then. Safe on any thread.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly object m_gate = new();
    private readonly Dictionary<ConnectionId, IServerTransport> m_owners = new();
    private readonly int m_maxConnections;
    private long m_last;

    public ConnectionRegistry(IOptions<NetworkOptions> options)
    {
        m_maxConnections = options.Value.MaxConnections;
    }

    public int Count
    {
        get
        {
            lock (m_gate)
            {
                return m_owners.Count;
            }
        }
    }

    /// <summary>
    ///     A new connection of <paramref name="owner" />; false, with no ID taken, when the server is full.
    /// </summary>
    public bool TryAdmit(IServerTransport owner, out ConnectionId connection)
    {
        lock (m_gate)
        {
            if (m_owners.Count >= m_maxConnections)
            {
                connection = default;
                return false;
            }

            connection = new ConnectionId(++m_last);
            m_owners.Add(connection, owner);
            return true;
        }
    }

    /// <summary>
    ///     The owner's report that the connection is gone; its ID is never handed out again.
    /// </summary>
    public void Release(ConnectionId connection)
    {
        lock (m_gate)
        {
            m_owners.Remove(connection);
        }
    }

    public bool TryGetOwner(ConnectionId connection, out IServerTransport? owner)
    {
        lock (m_gate)
        {
            return m_owners.TryGetValue(connection, out owner);
        }
    }
}
}
