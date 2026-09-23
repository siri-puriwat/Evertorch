using System;
using System.Collections.Concurrent;
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
public sealed class InboundQueue
{
    private readonly ConcurrentQueue<InboundEvent> m_events = new();
    private readonly int m_capacity;
    private int m_count;
    private long m_dropped;
    private long m_malformed;

    public InboundQueue(IOptions<NetworkOptions> options)
    {
        m_capacity = options.Value.MaxInboundEvents;
    }

    public int Count => Volatile.Read(ref m_count);

    /// <summary>
    ///     Events discarded because the queue was full: the tick thread is not keeping up or a peer is flooding.
    /// </summary>
    public long Dropped => Interlocked.Read(ref m_dropped);

    public long Malformed => Interlocked.Read(ref m_malformed);

    public void OnConnected(ConnectionId connection)
    {
        Enqueue(InboundEvent.Connected(connection), true);
    }

    public void OnDisconnected(ConnectionId connection)
    {
        Enqueue(InboundEvent.Disconnected(connection), true);
    }

    /// <summary>
    ///     Decodes one payload. Anything that is oversized, unknown, server-bound, on the wrong channel, or not exactly
    ///     a well-formed message becomes a <see cref="InboundEventKind.Malformed" /> event.
    /// </summary>
    public void OnPayload(ConnectionId connection, ProtocolChannel channel, ReadOnlySpan<byte> payload)
    {
        if (TryDecode(connection, channel, payload, out InboundEvent decoded))
        {
            Enqueue(decoded, false);
            return;
        }

        Interlocked.Increment(ref m_malformed);
        Enqueue(InboundEvent.Malformed(connection), false);
    }

    public bool TryDequeue(out InboundEvent inboundEvent)
    {
        if (!m_events.TryDequeue(out inboundEvent))
        {
            return false;
        }

        Interlocked.Decrement(ref m_count);
        return true;
    }

    private static bool TryDecode(
        ConnectionId connection,
        ProtocolChannel channel,
        ReadOnlySpan<byte> payload,
        out InboundEvent decoded)
    {
        decoded = default;
        if (payload.Length > ProtocolLimits.MaxClientPayloadBytes
            || !MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || !MessageRouting.IsClientToServer(opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel expectedChannel, out MessageDelivery _)
            || expectedChannel != channel)
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
            default:
                // A valid client message the server does not handle yet is treated like any other junk.
                return false;
        }
    }

    // Connection lifecycle events are never dropped: losing a disconnect would leak a session and its entity.
    private void Enqueue(InboundEvent inboundEvent, bool isLifecycle)
    {
        if (!isLifecycle && Volatile.Read(ref m_count) >= m_capacity)
        {
            Interlocked.Increment(ref m_dropped);
            return;
        }

        Interlocked.Increment(ref m_count);
        m_events.Enqueue(inboundEvent);
    }
}
}
