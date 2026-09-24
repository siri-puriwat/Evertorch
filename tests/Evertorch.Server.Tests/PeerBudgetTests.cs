using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Layer 1 of the abuse controls, on the network thread (Network Protocol §11): a budget of messages per peer per
///     tick, measured on an injected clock.
/// </summary>
[TestFixture]
public sealed class PeerBudgetTests
{
    private const int TickRate = 20;
    private static readonly TimeSpan OneTick = TimeSpan.FromSeconds(1.0 / TickRate);
    private static readonly ConnectionId Peer = new(1);
    private static readonly ConnectionId Other = new(2);

    private static InboundQueue CreateQueue(
        FakeClock clock,
        int burst = 10,
        int perTick = 3,
        int capacity = 4096,
        bool isEnabled = true)
    {
        return new InboundQueue(
            Options.Create(new NetworkOptions { MaxInboundEvents = capacity }),
            Options.Create(
                new AbuseOptions { Enabled = isEnabled, PeerMessageBurst = burst, PeerMessagesPerTick = perTick }),
            Options.Create(new SimulationOptions { TickRate = TickRate }),
            clock,
            TestInstruments.Create());
    }

    private static byte[] Move(uint sequence)
    {
        var message = new MoveInput(new MoveIntent(sequence, sequence, 1f, 0f));
        byte[] payload = new byte[MoveInput.EncodedLength];
        message.Write(payload);
        return payload;
    }

    private static byte[] Target(long entity)
    {
        var message = new TargetEntity(new EntityId(entity));
        byte[] payload = new byte[TargetEntity.EncodedLength];
        message.Write(payload);
        return payload;
    }

    private static void SendMoves(InboundQueue queue, ConnectionId connection, int count)
    {
        for (int index = 0; index < count; index++)
        {
            queue.OnPayload(connection, ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move((uint)index));
        }
    }

    private static void SendTargets(InboundQueue queue, ConnectionId connection, int count)
    {
        for (int index = 0; index < count; index++)
        {
            queue.OnPayload(connection, ProtocolChannel.Control, MessageDelivery.ReliableOrdered, Target(index + 1));
        }
    }

    private static List<InboundEvent> Drain(InboundQueue queue)
    {
        var events = new List<InboundEvent>();
        while (queue.TryDequeue(out InboundEvent inboundEvent))
        {
            events.Add(inboundEvent);
        }

        return events;
    }

    private static int Count(IEnumerable<InboundEvent> events, InboundEventKind kind, ConnectionId connection)
    {
        int count = 0;
        foreach (InboundEvent inboundEvent in events)
        {
            if (inboundEvent.Kind == kind && inboundEvent.Connection == connection)
            {
                count++;
            }
        }

        return count;
    }

    [TestCase(ProtocolChannel.Input, MessageDelivery.ReliableOrdered)]
    [TestCase(ProtocolChannel.Control, MessageDelivery.UnreliableSequenced)]
    public void Delivery_OtherThanTheRoutes_IsMalformed(ProtocolChannel channel, MessageDelivery delivery)
    {
        InboundQueue queue = CreateQueue(new FakeClock(), isEnabled: false);
        byte[] payload = channel == ProtocolChannel.Input ? Move(1) : Target(1);

        queue.OnPayload(Peer, channel, delivery, payload);

        Assert.That(queue.Malformed, Is.EqualTo(1));
        Assert.That(Count(Drain(queue), InboundEventKind.Malformed, Peer), Is.EqualTo(1));
    }

    [TestCase(20)]
    [TestCase(120)]
    public void HonestClient_AtAnyTickRate_StaysInsideTheBudget(int tickRate)
    {
        var clock = new FakeClock();
        var queue = new InboundQueue(
            Options.Create(new NetworkOptions()),
            Options.Create(new AbuseOptions { PeerMessageBurst = 10, PeerMessagesPerTick = 3 }),
            Options.Create(new SimulationOptions { TickRate = tickRate }),
            clock,
            TestInstruments.Create());
        queue.OnConnected(Peer);

        // One input a tick, and now and then a command, for ten seconds of play.
        for (int tick = 0; tick < tickRate * 10; tick++)
        {
            SendMoves(queue, Peer, 1);
            if (tick % 5 == 0)
            {
                SendTargets(queue, Peer, 1);
            }

            clock.Advance(TimeSpan.FromSeconds(1.0 / tickRate));
            Drain(queue);
        }

        Assert.That(queue.OverBudget, Is.Zero);
        Assert.That(queue.PeersLimited, Is.Zero);
    }

    [Test]
    public void Control_OverTheBudget_ClosesTheConnectionOnceAndDropsTheRest()
    {
        var clock = new FakeClock();
        InboundQueue queue = CreateQueue(clock);
        queue.OnConnected(Peer);

        SendTargets(queue, Peer, 13);
        List<InboundEvent> events = Drain(queue);

        Assert.That(Count(events, InboundEventKind.Target, Peer), Is.EqualTo(10));
        Assert.That(Count(events, InboundEventKind.RateLimited, Peer), Is.EqualTo(1), "told once");
        Assert.That(queue.PeersLimited, Is.EqualTo(1));
        Assert.That(queue.OverBudget, Is.EqualTo(3));

        clock.Advance(TimeSpan.FromSeconds(10));
        SendTargets(queue, Peer, 1);
        Assert.That(Drain(queue), Is.Empty, "a peer being closed sends nothing more, even with its budget back");
    }

    [Test]
    public void Delivery_UnknownToTheProtocol_IsMalformed()
    {
        InboundQueue queue = CreateQueue(new FakeClock(), isEnabled: false);

        queue.OnPayload(Peer, ProtocolChannel.Control, null, Target(1));

        Assert.That(queue.Malformed, Is.EqualTo(1));
    }

    [Test]
    public void FullQueue_ClosesItsHeaviestPeer_AndKeepsTheOtherPeersMessage()
    {
        InboundQueue queue = CreateQueue(new FakeClock(), 1000, capacity: 16);
        queue.OnConnected(Peer);
        queue.OnConnected(Other);
        queue.TryDequeue(out InboundEvent _);
        queue.TryDequeue(out InboundEvent _);

        SendTargets(queue, Peer, 16);
        SendTargets(queue, Other, 1);
        SendTargets(queue, Peer, 1);
        List<InboundEvent> events = Drain(queue);

        Assert.That(Count(events, InboundEventKind.RateLimited, Peer), Is.EqualTo(1));
        Assert.That(Count(events, InboundEventKind.Target, Other), Is.EqualTo(1), "the other peer's message is kept");
        Assert.That(Count(events, InboundEventKind.Target, Peer), Is.EqualTo(16), "the heaviest peer sends no more");
        Assert.That(queue.Dropped, Is.Zero);
    }

    [Test]
    public void FullQueue_WhenTheSenderIsTheHeaviest_DropsItsMessage()
    {
        InboundQueue queue = CreateQueue(new FakeClock(), 1000, capacity: 16);
        queue.OnConnected(Peer);
        queue.TryDequeue(out InboundEvent _);

        SendTargets(queue, Peer, 17);
        List<InboundEvent> events = Drain(queue);

        Assert.That(Count(events, InboundEventKind.Target, Peer), Is.EqualTo(16));
        Assert.That(Count(events, InboundEventKind.RateLimited, Peer), Is.EqualTo(1));
        Assert.That(queue.Dropped, Is.EqualTo(1));
    }

    [Test]
    public void Input_AfterTheBurst_GetsTheBudgetOfEachTick()
    {
        var clock = new FakeClock();
        InboundQueue queue = CreateQueue(clock);
        queue.OnConnected(Peer);
        SendMoves(queue, Peer, 10);
        Drain(queue);

        clock.Advance(OneTick);
        SendMoves(queue, Peer, 4);

        Assert.That(Count(Drain(queue), InboundEventKind.Move, Peer), Is.EqualTo(3));
        Assert.That(queue.OverBudget, Is.EqualTo(1));
    }

    [Test]
    public void Input_OverTheBudget_IsDroppedAndCountedWithoutClosingTheConnection()
    {
        InboundQueue queue = CreateQueue(new FakeClock());
        queue.OnConnected(Peer);

        SendMoves(queue, Peer, 12);
        List<InboundEvent> events = Drain(queue);

        Assert.That(Count(events, InboundEventKind.Move, Peer), Is.EqualTo(10));
        Assert.That(Count(events, InboundEventKind.RateLimited, Peer), Is.Zero);
        Assert.That(queue.OverBudget, Is.EqualTo(2));
        Assert.That(queue.PeersLimited, Is.Zero);
    }

    [Test]
    public void LimitsOff_NothingIsDroppedOrClosed()
    {
        InboundQueue queue = CreateQueue(new FakeClock(), isEnabled: false);
        queue.OnConnected(Peer);

        SendTargets(queue, Peer, 500);

        Assert.That(Count(Drain(queue), InboundEventKind.Target, Peer), Is.EqualTo(500));
        Assert.That(queue.OverBudget, Is.Zero);
    }
}
}
