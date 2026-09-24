using System;
using System.Collections.Generic;
using Evertorch.Client;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Joins the real client code to the in-memory test server: what the client sends goes into the server's inbound
///     queue as the network thread would put it there, and what the server sent comes back on the next poll.
/// </summary>
internal sealed class LoopbackClientTransport : IClientTransport
{
    private readonly TestServer m_server;
    private ConnectionId m_connection;
    private int m_delivered;
    private bool m_hasAnnouncedConnect;
    private bool m_isClosed;

    public LoopbackClientTransport(TestServer server)
    {
        m_server = server;
    }

    public ConnectionId Connection => m_connection;

    public bool IsConnected { get; private set; }

    public int RoundTripMilliseconds => 0;

    public void Connect(string host, int port)
    {
        m_connection = m_server.Connect();
        IsConnected = true;
    }

    public void Disconnect()
    {
        if (IsConnected)
        {
            m_server.Disconnect(m_connection);
            IsConnected = false;
            m_isClosed = true;
        }
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        if (IsConnected)
        {
            m_server.Inbound.OnPayload(m_connection, channel, delivery, payload);
        }
    }

    public void Poll(IClientTransportListener listener)
    {
        if (IsConnected && !m_hasAnnouncedConnect)
        {
            m_hasAnnouncedConnect = true;
            listener.OnConnected();
        }

        IReadOnlyList<InMemoryServerTransport.SentMessage> sent = m_server.Transport.SentTo(m_connection);
        while (m_delivered < sent.Count)
        {
            InMemoryServerTransport.SentMessage message = sent[m_delivered];
            m_delivered++;
            listener.OnPayload(message.Channel, message.Payload);
        }

        if (IsConnected && m_server.Transport.Disconnects.ContainsKey(m_connection))
        {
            IsConnected = false;
            listener.OnDisconnected(TransportDisconnectCause.ClosedByServer, ReadOnlySpan<byte>.Empty);
        }
        else if (m_isClosed)
        {
            m_isClosed = false;
            listener.OnDisconnected(TransportDisconnectCause.ClosedLocally, ReadOnlySpan<byte>.Empty);
        }
    }
}
}
