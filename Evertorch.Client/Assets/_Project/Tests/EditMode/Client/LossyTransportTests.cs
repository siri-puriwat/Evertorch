using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class LossyTransportTests
{
    private static List<uint> RunSeeded(int seed)
    {
        var rig = new Rig(seed)
        {
            Lossy = { LatencyMilliseconds = 50, JitterMilliseconds = 60, LossPercent = 10, ReorderPercent = 10 }
        };
        for (uint sequence = 1; sequence <= 200; sequence++)
        {
            rig.Lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(sequence));
            rig.AdvanceTo(rig.Now + 0.05);
        }

        rig.AdvanceTo(rig.Now + 1.0);
        return rig.Inner.Sent.Select(sent => SequenceOf(sent.Payload)).ToList();
    }

    private static byte[] Move(uint sequence)
    {
        byte[] payload = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(sequence, sequence, 1f, 0f)).Write(payload);
        return payload;
    }

    private static uint SequenceOf(byte[] payload)
    {
        Assert.That(MoveInput.TryRead(payload, out MoveInput input), Is.True);
        return input.Intent.Sequence;
    }

    private static byte[] Snapshot(uint tick)
    {
        var snapshot = new EntitySnapshot(tick, 0, new EntityState[0]);
        byte[] payload = new byte[snapshot.GetEncodedLength()];
        snapshot.Write(payload);
        return payload;
    }

    private sealed class Rig
    {
        public Rig(int seed)
        {
            Inner = new FakeClientTransport();
            Lossy = new LossyTransport(Inner, seed, () => Now);
        }

        public FakeClientTransport Inner { get; }

        public LossyTransport Lossy { get; }

        public RecordingListener Listener { get; } = new();

        public double Now { get; private set; }

        public void AdvanceTo(double seconds)
        {
            Now = seconds;
            Lossy.Poll(Listener);
        }
    }

    private sealed class RecordingListener : IClientTransportListener
    {
        public int Connected { get; private set; }

        public int Disconnected { get; private set; }

        public List<byte[]> Payloads { get; } = new();

        public void OnConnected()
        {
            Connected++;
        }

        public void OnDisconnected(TransportDisconnectCause cause, ReadOnlySpan<byte> notice)
        {
            Disconnected++;
        }

        public void OnPayload(ProtocolChannel channel, ReadOnlySpan<byte> payload)
        {
            Payloads.Add(payload.ToArray());
        }
    }

    [Test]
    public void ConnectionEvents_PassStraightThrough()
    {
        var rig = new Rig(1) { Lossy = { LatencyMilliseconds = 500 } };

        rig.Lossy.Connect("127.0.0.1", 7777);
        rig.Inner.CompleteConnect();
        rig.AdvanceTo(0.0);
        rig.Inner.DropConnection(TransportDisconnectCause.TimedOut, new byte[0]);
        rig.AdvanceTo(0.0);

        Assert.That(rig.Inner.Host, Is.EqualTo("127.0.0.1"));
        Assert.That(rig.Listener.Connected, Is.EqualTo(1));
        Assert.That(rig.Listener.Disconnected, Is.EqualTo(1));
    }

    [Test]
    public void Receive_WithLatency_ReachesTheListenerOnlyAfterTheDelay()
    {
        var rig = new Rig(1) { Lossy = { LatencyMilliseconds = 100 } };

        rig.Inner.Deliver(ProtocolChannel.State, Snapshot(1));
        rig.AdvanceTo(0.05);
        int before = rig.Listener.Payloads.Count;
        rig.AdvanceTo(0.2);

        Assert.That(before, Is.EqualTo(0));
        Assert.That(rig.Listener.Payloads.Count, Is.EqualTo(1));
    }

    [Test]
    public void ReliableMessages_KeepTheirOrderWhateverTheSettings()
    {
        var rig = new Rig(3)
        {
            Lossy = { LatencyMilliseconds = 80, JitterMilliseconds = 200, ReorderPercent = 100, LossPercent = 50 }
        };

        for (byte index = 0; index < 50; index++)
        {
            rig.Lossy.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered, new byte[] { 0x02, 0x00, index });
            rig.AdvanceTo(rig.Now + 0.01);
        }

        rig.AdvanceTo(rig.Now + 1.0);

        Assert.That(
            rig.Inner.Sent.Select(sent => sent.Payload[2]),
            Is.EqualTo(Enumerable.Range(0, 50).Select(index => (byte)index)));
    }

    [Test]
    public void RoundTrip_IncludesTheAddedLatencyBothWays()
    {
        var rig = new Rig(1) { Lossy = { LatencyMilliseconds = 75 } };

        Assert.That(rig.Lossy.RoundTripMilliseconds, Is.EqualTo(rig.Inner.RoundTripMilliseconds + 150));
    }

    [Test]
    public void SameSeedAndClock_GiveTheSameOutcome()
    {
        List<uint> first = RunSeeded(11);
        List<uint> second = RunSeeded(11);
        List<uint> other = RunSeeded(12);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }

    [Test]
    public void Send_WithLatency_ReachesTheInnerTransportOnlyAfterTheDelay()
    {
        var rig = new Rig(1) { Lossy = { LatencyMilliseconds = 100 } };

        rig.Lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(1));
        rig.AdvanceTo(0.099);
        int before = rig.Inner.Sent.Count;
        rig.AdvanceTo(0.101);

        Assert.That(before, Is.EqualTo(0));
        Assert.That(rig.Inner.Sent.Count, Is.EqualTo(1));
        Assert.That(rig.Inner.Sent[0].Channel, Is.EqualTo(ProtocolChannel.Input));
        Assert.That(rig.Inner.Sent[0].Payload, Is.EqualTo(Move(1)));
    }

    [Test]
    public void Send_WithPartialLoss_DropsRoughlyThatShare()
    {
        var rig = new Rig(7) { Lossy = { LossPercent = 20 } };

        for (uint sequence = 1; sequence <= 1000; sequence++)
        {
            rig.Lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(sequence));
        }

        Assert.That(rig.Lossy.Dropped, Is.InRange(150, 250));
    }

    [Test]
    public void Send_WithTotalLoss_DropsUnreliableMessagesButNeverReliableOnes()
    {
        var rig = new Rig(1) { Lossy = { LossPercent = 100 } };

        rig.Lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(1));
        rig.Lossy.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered, new byte[] { 0x02, 0x00 });
        rig.AdvanceTo(1.0);

        Assert.That(rig.Inner.Sent.Count, Is.EqualTo(1));
        Assert.That(rig.Inner.Sent[0].Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(rig.Lossy.Dropped, Is.EqualTo(1));
    }

    [Test]
    public void UnreliableMessages_WithReordering_ArriveOutOfOrder()
    {
        var rig = new Rig(5) { Lossy = { LatencyMilliseconds = 20, ReorderPercent = 30 } };

        for (uint sequence = 1; sequence <= 100; sequence++)
        {
            rig.Lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(sequence));
            rig.AdvanceTo(rig.Now + 0.05);
        }

        rig.AdvanceTo(rig.Now + 1.0);

        var arrived = rig.Inner.Sent.Select(sent => SequenceOf(sent.Payload)).ToList();
        Assert.That(arrived.Count, Is.EqualTo(100), "reordering alone loses nothing");
        Assert.That(arrived, Is.Not.Ordered);
        Assert.That(rig.Lossy.Reordered, Is.GreaterThan(0));
    }

    [Test]
    public void WhenLatencyIsSwitchedOff_MessagesAlreadyHeldStillGoOutFirst()
    {
        var rig = new Rig(1) { Lossy = { LatencyMilliseconds = 100 } };
        rig.Lossy.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered, new byte[] { 0x02, 0x00, 1 });

        rig.Lossy.LatencyMilliseconds = 0;
        rig.Lossy.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered, new byte[] { 0x02, 0x00, 2 });
        rig.AdvanceTo(0.2);

        Assert.That(rig.Inner.Sent.Select(sent => sent.Payload[2]), Is.EqualTo(new byte[] { 1, 2 }));
    }

    [Test]
    public void WithNothingToSimulate_CostsNoExtraPollEitherWay()
    {
        double clock = 0.0;
        var inner = new FakeClientTransport();
        var lossy = new LossyTransport(inner, 1, () => clock += 0.001);
        var listener = new RecordingListener();

        lossy.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Move(1));
        int sentBeforeAnyPoll = inner.Sent.Count;
        inner.Deliver(ProtocolChannel.State, Snapshot(1));
        lossy.Poll(listener);

        Assert.That(sentBeforeAnyPoll, Is.EqualTo(1), "outbound goes straight through");
        Assert.That(listener.Payloads.Count, Is.EqualTo(1), "inbound arrives in the poll that received it");
    }
}
}
