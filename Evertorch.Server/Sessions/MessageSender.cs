using System;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Encodes server messages into one reusable buffer. Tick thread only, which is what makes the shared buffer safe.
/// </summary>
public sealed class MessageSender
{
    // Larger than any message sent today; snapshots are sized to fit one unreliable datagram well below this.
    private const int BufferBytes = 2048;

    private readonly IOutboundMessages m_outbound;
    private readonly byte[] m_buffer = new byte[BufferBytes];

    public MessageSender(IOutboundMessages outbound)
    {
        m_outbound = outbound;
    }

    public void Send(ConnectionId connection, ServerHello message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, WorldEntered message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntitySpawn message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityDespawn message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntitySnapshot message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, TargetChanged message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, AttackStarted message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, Damage message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityDied message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterHealth message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, EntityRevived message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CharacterList message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, CreateCharacterResult message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Send(ConnectionId connection, ItemDropped message)
    {
        m_outbound.Send(connection, m_buffer.AsSpan(0, message.Write(m_buffer)));
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason)
    {
        m_outbound.Disconnect(connection, reason, string.Empty);
    }
}
}
