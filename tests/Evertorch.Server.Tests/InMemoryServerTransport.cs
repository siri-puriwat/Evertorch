using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
/// Records what the server sends, per connection and in order, instead of putting it on a socket.
/// </summary>
internal sealed class InMemoryServerTransport : IOutboundMessages
{
    private readonly Dictionary<ConnectionId, List<SentMessage>> m_sent =
        new Dictionary<ConnectionId, List<SentMessage>>();

    public Dictionary<ConnectionId, DisconnectReason> Disconnects { get; } =
        new Dictionary<ConnectionId, DisconnectReason>();

    /// <summary>
    /// Sends to these connections throw, standing in for any defect while one peer's input is handled.
    /// </summary>
    public HashSet<ConnectionId> FailSendsTo { get; } = new HashSet<ConnectionId>();

    public void Send(ConnectionId connection, ReadOnlySpan<byte> payload)
    {
        if (FailSendsTo.Contains(connection))
        {
            throw new InvalidOperationException("Simulated failure while sending.");
        }

        if (!MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery))
        {
            throw new InvalidOperationException("The server sent a payload without a routable opcode.");
        }

        if (!m_sent.TryGetValue(connection, out List<SentMessage>? messages))
        {
            messages = new List<SentMessage>();
            m_sent.Add(connection, messages);
        }

        messages.Add(new SentMessage(opcode, channel, delivery, payload.ToArray()));
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason, string message)
    {
        Disconnects[connection] = reason;
    }

    public IReadOnlyList<SentMessage> SentTo(ConnectionId connection)
    {
        return m_sent.TryGetValue(connection, out List<SentMessage>? messages) ? messages : new List<SentMessage>();
    }

    /// <summary>
    /// The reliable stream only, which is what session and visibility tests reason about; snapshots flow every tick.
    /// </summary>
    public IReadOnlyList<SentMessage> ControlSentTo(ConnectionId connection)
    {
        return SentTo(connection).Where(message => message.Channel == ProtocolChannel.Control).ToArray();
    }

    public IReadOnlyList<MessageOpcode> ControlOpcodesSentTo(ConnectionId connection)
    {
        return ControlSentTo(connection).Select(message => message.Opcode).ToArray();
    }

    public IReadOnlyList<EntitySnapshot> SnapshotsSentTo(ConnectionId connection)
    {
        List<EntitySnapshot> snapshots = new List<EntitySnapshot>();
        foreach (SentMessage message in SentTo(connection))
        {
            if (message.Opcode == MessageOpcode.EntitySnapshot
                && EntitySnapshot.TryRead(message.Payload, out EntitySnapshot? snapshot)
                && snapshot != null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    public void ClearSent()
    {
        m_sent.Clear();
    }

    internal sealed class SentMessage
    {
        public SentMessage(MessageOpcode opcode, ProtocolChannel channel, MessageDelivery delivery, byte[] payload)
        {
            Opcode = opcode;
            Channel = channel;
            Delivery = delivery;
            Payload = payload;
        }

        public MessageOpcode Opcode { get; }

        public ProtocolChannel Channel { get; }

        public MessageDelivery Delivery { get; }

        public byte[] Payload { get; }
    }
}
}
