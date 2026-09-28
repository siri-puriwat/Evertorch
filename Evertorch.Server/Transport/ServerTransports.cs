using System;
using System.Collections.Generic;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Every transport the server listens on, as the one <see cref="IServerTransport" /> the rest of the server sees
///     (System Architecture §8): it starts, closes admission for, and stops them all, sends through the transport that
///     owns each connection, and sums their statistics. Sessions and the world never learn which transport a player
///     uses.
/// </summary>
public sealed class ServerTransports : IServerTransport
{
    private readonly IReadOnlyList<IServerTransport> m_transports;
    private readonly ConnectionRegistry m_connections;

    public ServerTransports(IReadOnlyList<IServerTransport> transports, ConnectionRegistry connections)
    {
        if (transports.Count == 0)
        {
            throw new ArgumentException("The server needs at least one transport.", nameof(transports));
        }

        m_transports = transports;
        m_connections = connections;
    }

    public bool IsAdmissionOpen
    {
        get
        {
            foreach (IServerTransport transport in m_transports)
            {
                if (!transport.IsAdmissionOpen)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void Start()
    {
        foreach (IServerTransport transport in m_transports)
        {
            transport.Start();
        }
    }

    public void CloseAdmission()
    {
        foreach (IServerTransport transport in m_transports)
        {
            transport.CloseAdmission();
        }
    }

    public void Stop(DisconnectReason reason, string message)
    {
        foreach (IServerTransport transport in m_transports)
        {
            transport.Stop(reason, message);
        }
    }

    public void Send(ConnectionId connection, ReadOnlySpan<byte> payload)
    {
        if (m_connections.TryGetOwner(connection, out IServerTransport? owner))
        {
            owner!.Send(connection, payload);
        }
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason, string message)
    {
        if (m_connections.TryGetOwner(connection, out IServerTransport? owner))
        {
            owner!.Disconnect(connection, reason, message);
        }
    }

    // A connection that left on its own is no longer in the registry, but its transport still remembers its address
    // for a while; every other transport ignores an ID it never had.
    public void CoolDownAddress(ConnectionId connection)
    {
        if (m_connections.TryGetOwner(connection, out IServerTransport? owner))
        {
            owner!.CoolDownAddress(connection);
            return;
        }

        foreach (IServerTransport transport in m_transports)
        {
            transport.CoolDownAddress(connection);
        }
    }

    public TransportStatistics GetStatistics()
    {
        long bytesReceived = 0;
        long bytesSent = 0;
        long packetsReceived = 0;
        long packetsSent = 0;
        long packetsLost = 0;
        foreach (IServerTransport transport in m_transports)
        {
            TransportStatistics statistics = transport.GetStatistics();
            bytesReceived += statistics.BytesReceived;
            bytesSent += statistics.BytesSent;
            packetsReceived += statistics.PacketsReceived;
            packetsSent += statistics.PacketsSent;
            packetsLost += statistics.PacketsLost;
        }

        return new TransportStatistics(bytesReceived, bytesSent, packetsReceived, packetsSent, packetsLost);
    }

    public bool TryGetRoundTripTime(ConnectionId connection, out int milliseconds)
    {
        milliseconds = 0;
        return m_connections.TryGetOwner(connection, out IServerTransport? owner)
            && owner!.TryGetRoundTripTime(connection, out milliseconds);
    }
}
}
