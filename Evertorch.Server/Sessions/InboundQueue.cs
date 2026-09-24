using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The only structure network threads and the tick thread share. Network threads decode and enqueue here and touch
///     nothing else; the tick thread drains it at the start of a tick and is the sole writer of sessions and the world.
/// </summary>
/// <remarks>
///     Layer 1 of the abuse controls lives here, on the network thread (Network Protocol §11): each peer has a budget
///     of messages per tick. Over it, input is dropped, and the tick thread is told how much so it can score it; a
///     reliable message closes the connection instead, because the transport has already acknowledged it and a
///     sequenced command must not go unanswered. A full queue closes its heaviest peer rather than dropping everyone's
///     reliable messages.
/// </remarks>
public sealed class InboundQueue
{
    private readonly ConcurrentQueue<InboundEvent> m_events = new();
    private readonly ConcurrentDictionary<ConnectionId, PeerBudget> m_peers = new();
    private readonly int m_capacity;
    private readonly bool m_isLimited;
    private readonly double m_messagesPerSecond;
    private readonly double m_burst;
    private readonly IMonotonicClock m_clock;
    private readonly ServerInstruments m_instruments;
    private int m_count;
    private long m_dropped;
    private long m_malformed;
    private long m_overBudget;
    private long m_peersLimited;

    public InboundQueue(
        IOptions<NetworkOptions> network,
        IOptions<AbuseOptions> abuse,
        IOptions<SimulationOptions> simulation,
        IMonotonicClock clock,
        ServerInstruments instruments)
    {
        m_capacity = network.Value.MaxInboundEvents;
        m_isLimited = abuse.Value.Enabled;
        m_messagesPerSecond = (double)abuse.Value.PeerMessagesPerTick * simulation.Value.TickRate;
        m_burst = abuse.Value.PeerMessageBurst;
        m_clock = clock;
        m_instruments = instruments;
    }

    public int Count => Volatile.Read(ref m_count);

    /// <summary>
    ///     Events discarded because the queue was full: the tick thread is not keeping up or a peer is flooding.
    /// </summary>
    public long Dropped => Interlocked.Read(ref m_dropped);

    public long Malformed => Interlocked.Read(ref m_malformed);

    /// <summary>
    ///     Messages dropped, or that closed their connection, because their peer was over its budget.
    /// </summary>
    public long OverBudget => Interlocked.Read(ref m_overBudget);

    /// <summary>
    ///     Connections this queue asked the tick thread to close with <c>RateLimited</c>.
    /// </summary>
    public long PeersLimited => Interlocked.Read(ref m_peersLimited);

    /// <summary>
    ///     Peers with a message budget: one for each connection the transport has not yet reported closed.
    /// </summary>
    public int TrackedPeers => m_peers.Count;

    public void OnConnected(ConnectionId connection)
    {
        m_peers[connection] = new PeerBudget(m_clock.Elapsed, m_burst);
        EnqueueLifecycle(InboundEvent.Connected(connection));
    }

    public void OnDisconnected(ConnectionId connection)
    {
        m_peers.TryRemove(connection, out PeerBudget? _);
        EnqueueLifecycle(InboundEvent.Disconnected(connection));
    }

    /// <summary>
    ///     The transport finished closing a connection the session layer closed itself. Its session is already gone, so
    ///     nothing is enqueued; only the peer's budget is forgotten.
    /// </summary>
    public void OnClosed(ConnectionId connection)
    {
        m_peers.TryRemove(connection, out PeerBudget? _);
    }

    /// <summary>
    ///     Decodes one payload. Anything that is oversized, unknown, server-bound, on the wrong channel or delivery, or
    ///     not exactly a well-formed message becomes a <see cref="InboundEventKind.Malformed" /> event.
    /// </summary>
    /// <param name="delivery">How the transport delivered it; null for a method the protocol never uses.</param>
    public void OnPayload(
        ConnectionId connection,
        ProtocolChannel channel,
        MessageDelivery? delivery,
        ReadOnlySpan<byte> payload)
    {
        // A peer the transport has already reported closed has no budget left, and no session to act on it.
        PeerBudget? peer = null;
        if (m_isLimited && m_peers.TryGetValue(connection, out peer) && !Admit(connection, channel, peer))
        {
            return;
        }

        if (TryDecode(connection, channel, delivery, payload, out InboundEvent decoded))
        {
            EnqueueMessage(decoded, peer);
            return;
        }

        Interlocked.Increment(ref m_malformed);
        EnqueueMessage(InboundEvent.Malformed(connection), peer);
    }

    public bool TryDequeue(out InboundEvent inboundEvent)
    {
        if (!m_events.TryDequeue(out inboundEvent))
        {
            return false;
        }

        Interlocked.Decrement(ref m_count);
        if (!m_peers.TryGetValue(inboundEvent.Connection, out PeerBudget? peer))
        {
            return true;
        }

        if (inboundEvent.Kind == InboundEventKind.InputDropped)
        {
            inboundEvent = InboundEvent.InputDropped(inboundEvent.Connection, peer.TakeDroppedInputs());
        }
        else if (!IsLifecycle(inboundEvent.Kind))
        {
            peer.OnDequeued();
        }

        return true;
    }

    private static bool IsLifecycle(InboundEventKind kind)
    {
        return kind == InboundEventKind.Connected
            || kind == InboundEventKind.Disconnected
            || kind == InboundEventKind.RateLimited
            || kind == InboundEventKind.InputDropped;
    }

    private static bool TryDecode(
        ConnectionId connection,
        ProtocolChannel channel,
        MessageDelivery? delivery,
        ReadOnlySpan<byte> payload,
        out InboundEvent decoded)
    {
        decoded = default;

        // Checked before the size and the decode: a hello of another version may be longer or laid out differently,
        // and must still be told ProtocolMismatch rather than dropped as malformed.
        if (channel == ProtocolChannel.Control
            && delivery == MessageDelivery.ReliableOrdered
            && ClientHello.TryReadProtocolVersion(payload, out ushort protocolVersion)
            && protocolVersion != ProtocolConstants.ProtocolVersion)
        {
            decoded = InboundEvent.ForHello(
                connection,
                new ClientHello(protocolVersion, string.Empty, 0, string.Empty));
            return true;
        }

        if (payload.Length > ProtocolLimits.MaxClientPayloadBytes
            || !MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || !MessageRouting.IsClientToServer(opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel expectedChannel, out MessageDelivery expected)
            || expectedChannel != channel
            || delivery != expected)
        {
            return false;
        }

        switch (opcode)
        {
            case MessageOpcode.ClientHello:
                if (!ClientHello.TryRead(payload, out ClientHello? hello) || hello == null)
                {
                    return false;
                }

                decoded = InboundEvent.ForHello(connection, hello);
                return true;
            case MessageOpcode.EnterWorldRequest:
                if (!EnterWorldRequest.TryRead(payload, out EnterWorldRequest request))
                {
                    return false;
                }

                decoded = InboundEvent.ForEnterWorld(connection, request);
                return true;
            case MessageOpcode.MoveInput:
                if (!MoveInput.TryRead(payload, out MoveInput move))
                {
                    return false;
                }

                decoded = InboundEvent.ForMove(connection, move.Intent);
                return true;
            case MessageOpcode.StopMovement:
                if (!StopMovement.TryRead(payload, out StopMovement stop))
                {
                    return false;
                }

                decoded = InboundEvent.ForMove(connection, new MoveIntent(stop.Sequence, stop.ClientTick, 0f, 0f));
                return true;
            case MessageOpcode.TargetEntity:
                if (!TargetEntity.TryRead(payload, out TargetEntity target))
                {
                    return false;
                }

                decoded = InboundEvent.ForTarget(connection, target.Target);
                return true;
            case MessageOpcode.AttackEntity:
                if (!AttackEntity.TryRead(payload, out AttackEntity attack))
                {
                    return false;
                }

                decoded = InboundEvent.ForAttack(connection, attack.Target, attack.CommandSequence);
                return true;
            case MessageOpcode.PickupItem:
                if (!PickupItem.TryRead(payload, out PickupItem pickup))
                {
                    return false;
                }

                decoded = InboundEvent.ForPickup(connection, pickup.Drop, pickup.CommandSequence);
                return true;
            case MessageOpcode.CancelAction:
                if (!CancelAction.TryRead(payload, out CancelAction cancel))
                {
                    return false;
                }

                decoded = InboundEvent.ForCommand(InboundEventKind.Cancel, connection, cancel.CommandSequence);
                return true;
            case MessageOpcode.Respawn:
                if (!Respawn.TryRead(payload, out Respawn respawn))
                {
                    return false;
                }

                decoded = InboundEvent.ForCommand(InboundEventKind.Respawn, connection, respawn.CommandSequence);
                return true;
            case MessageOpcode.Logout:
                if (!Logout.TryRead(payload, out Logout logout))
                {
                    return false;
                }

                decoded = InboundEvent.ForCommand(InboundEventKind.Logout, connection, logout.CommandSequence);
                return true;
            case MessageOpcode.InventoryResyncRequest:
                if (!InventoryResyncRequest.TryRead(payload, out _))
                {
                    return false;
                }

                decoded = InboundEvent.ForInventoryResync(connection);
                return true;
            case MessageOpcode.CreateCharacter:
                if (!CreateCharacter.TryRead(payload, out CreateCharacter? create) || create == null)
                {
                    return false;
                }

                decoded = InboundEvent.ForCreateCharacter(connection, create.Name);
                return true;
            default:
                // A valid client message the server does not handle yet is treated like any other junk.
                return false;
        }
    }

    // Whether the payload may go on to decoding. A peer already being closed sends nothing more to the tick thread.
    private bool Admit(ConnectionId connection, ProtocolChannel channel, PeerBudget peer)
    {
        if (peer.IsLimited)
        {
            Interlocked.Increment(ref m_overBudget);
            return false;
        }

        if (peer.TryTake(m_clock.Elapsed, m_messagesPerSecond, m_burst))
        {
            return true;
        }

        Interlocked.Increment(ref m_overBudget);
        if (channel == ProtocolChannel.Input)
        {
            m_instruments.RecordRateLimited(ServerInstruments.PeerInputLimit);
            if (peer.OnInputDropped())
            {
                EnqueueLifecycle(InboundEvent.InputDropped(connection, 0));
            }

            return false;
        }

        m_instruments.RecordRateLimited(ServerInstruments.PeerControlLimit);
        Limit(connection, peer);
        return false;
    }

    private void Limit(ConnectionId connection, PeerBudget peer)
    {
        if (peer.MarkLimited())
        {
            Interlocked.Increment(ref m_peersLimited);
            EnqueueLifecycle(InboundEvent.RateLimited(connection));
        }
    }

    // Lifecycle events are never dropped: losing a disconnect would leak a session and its entity. They stay bounded,
    // because a peer has at most one close and one report of dropped input waiting.
    private void EnqueueLifecycle(InboundEvent inboundEvent)
    {
        Interlocked.Increment(ref m_count);
        m_events.Enqueue(inboundEvent);
    }

    // With the limits off a full queue drops the message and counts it. With them on, the queue closes whichever peer
    // has the most waiting and keeps this message, unless it is that peer's own.
    private void EnqueueMessage(InboundEvent inboundEvent, PeerBudget? peer)
    {
        if (Volatile.Read(ref m_count) >= m_capacity &&
            (peer == null || IsHeaviestAfterLimiting(inboundEvent.Connection)))
        {
            Interlocked.Increment(ref m_dropped);
            return;
        }

        peer?.OnEnqueued();
        Interlocked.Increment(ref m_count);
        m_events.Enqueue(inboundEvent);
    }

    // Closes the peer with the most messages waiting. True when that is the sender itself.
    private bool IsHeaviestAfterLimiting(ConnectionId sender)
    {
        ConnectionId heaviest = default;
        PeerBudget? heaviestPeer = null;
        foreach (KeyValuePair<ConnectionId, PeerBudget> entry in m_peers)
        {
            if (!entry.Value.IsLimited && (heaviestPeer == null || entry.Value.Pending > heaviestPeer.Pending))
            {
                heaviest = entry.Key;
                heaviestPeer = entry.Value;
            }
        }

        if (heaviestPeer == null)
        {
            return true;
        }

        m_instruments.RecordRateLimited(ServerInstruments.QueueFullLimit);
        Limit(heaviest, heaviestPeer);
        return heaviest == sender;
    }

    /// <summary>
    ///     One peer's token bucket, refilled from the injected clock, how many of its messages wait in the queue, and how
    ///     much of its input was dropped since the tick thread last asked.
    /// </summary>
    private sealed class PeerBudget
    {
        private readonly object m_gate = new();
        private double m_tokens;
        private TimeSpan m_refilledAt;
        private int m_pending;
        private int m_isLimited;
        private int m_droppedInputs;
        private int m_isReporting;

        public PeerBudget(TimeSpan now, double burst)
        {
            m_tokens = burst;
            m_refilledAt = now;
        }

        public bool IsLimited => Volatile.Read(ref m_isLimited) != 0;

        public int Pending => Volatile.Read(ref m_pending);

        public bool TryTake(TimeSpan now, double perSecond, double burst)
        {
            lock (m_gate)
            {
                if (now > m_refilledAt)
                {
                    m_tokens = Math.Min(burst, m_tokens + (now - m_refilledAt).TotalSeconds * perSecond);
                    m_refilledAt = now;
                }

                if (m_tokens < 1d)
                {
                    return false;
                }

                m_tokens -= 1d;
                return true;
            }
        }

        // True only for the first caller, so the tick thread is told once.
        public bool MarkLimited()
        {
            return Interlocked.Exchange(ref m_isLimited, 1) == 0;
        }

        public void OnEnqueued()
        {
            Interlocked.Increment(ref m_pending);
        }

        public void OnDequeued()
        {
            Interlocked.Decrement(ref m_pending);
        }

        // True when no report is waiting, so the caller enqueues one.
        public bool OnInputDropped()
        {
            Interlocked.Increment(ref m_droppedInputs);
            return Interlocked.Exchange(ref m_isReporting, 1) == 0;
        }

        // The flag is cleared before the count is taken, so a drop in between is either in this count or reported
        // again; none is lost.
        public int TakeDroppedInputs()
        {
            Volatile.Write(ref m_isReporting, 0);
            return Interlocked.Exchange(ref m_droppedInputs, 0);
        }
    }
}
}
