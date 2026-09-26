using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class InboundQueueTests
{
    private static readonly ConnectionId Peer = new(1);

    [TestCase(new byte[0])]
    [TestCase(new byte[] { 0x01 })]
    [TestCase(new byte[] { 0x00, 0x00 })]
    [TestCase(new byte[] { 0x99, 0x00, 0x01 })]
    [TestCase(new byte[] { 0x01, 0x00, 0x01 })]
    [TestCase(new byte[] { 0x02, 0x00, 0x01, 0x02 })]
    public void Message_ThatIsNotAWellFormedClientMessage_IsRejected(byte[] payload)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        AssertOnlyMalformed(queue, 1);
    }

    private static InboundQueue CreateQueue(int capacity)
    {
        return new InboundQueue(
            Options.Create(new NetworkOptions { MaxInboundEvents = capacity }),
            Options.Create(new AbuseOptions { Enabled = false }),
            Options.Create(new SimulationOptions()),
            new FakeClock(),
            TestInstruments.Create());
    }

    private static byte[] Hello()
    {
        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, 1, "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static byte[] UseSkillPayload()
    {
        var useSkill = new UseSkill(new SkillDefinitionId("skill.strike"), new EntityId(42), 7);
        byte[] payload = new byte[useSkill.GetEncodedLength()];
        useSkill.Write(payload);
        return payload;
    }

    private static byte[] EquipPayload()
    {
        byte[] payload = new byte[EquipItem.EncodedLength];
        new EquipItem(41, 9).Write(payload);
        return payload;
    }

    private static byte[] UnequipPayload()
    {
        byte[] payload = new byte[UnequipItem.EncodedLength];
        new UnequipItem(EquipmentSlot.Weapon, 9).Write(payload);
        return payload;
    }

    private static byte[] UseItemPayload()
    {
        byte[] payload = new byte[UseItem.EncodedLength];
        new UseItem(41, 9).Write(payload);
        return payload;
    }

    private static byte[] BuyPayload()
    {
        var message = new BuyItem(new EntityId(13), new ItemDefinitionId("item.a"), 2, 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    private static byte[] SellPayload()
    {
        byte[] payload = new byte[SellItem.EncodedLength];
        new SellItem(new EntityId(13), 41, 2, 9).Write(payload);
        return payload;
    }

    private static byte[] With(byte[] payload, int start, int count, byte value)
    {
        byte[] changed = (byte[])payload.Clone();
        changed.AsSpan(start, count).Fill(value);
        return changed;
    }

    // Milestone 6's commands cut short, one byte too long, or carrying a value no client may send (Network Protocol
    // §11). The item row sits after the opcode, the slot too, and the skill ID's text after its two-byte length.
    private static IEnumerable<TestCaseData> MalformedItemAndSkillCommands()
    {
        byte[] useSkill = UseSkillPayload();
        byte[] equip = EquipPayload();
        byte[] unequip = UnequipPayload();
        byte[] useItem = UseItemPayload();
        byte[] buy = BuyPayload();
        byte[] sell = SellPayload();
        foreach ((string name, byte[] payload) in new[]
                 {
                     ("UseSkill", useSkill), ("EquipItem", equip), ("UnequipItem", unequip), ("UseItem", useItem),
                     ("BuyItem", buy), ("SellItem", sell)
                 })
        {
            yield return new TestCaseData(payload.Take(payload.Length - 1).ToArray()).SetName($"{name} cut short");
            yield return new TestCaseData(payload.Append((byte)0).ToArray()).SetName($"{name} one byte too long");
        }

        const int text = 4;
        yield return new TestCaseData(With(useSkill, text + 5, 1, 0x20)).SetName("UseSkill naming no skill");
        yield return new TestCaseData(With(useSkill, text, 1, 0xFF)).SetName("UseSkill of broken UTF-8");
        yield return new TestCaseData(With(useSkill, text - 2, 2, 0xFF)).SetName("UseSkill longer than any ID");
        yield return new TestCaseData(With(equip, 2, 8, 0x00)).SetName("EquipItem of row 0");
        yield return new TestCaseData(With(equip, 2, 8, 0xFF)).SetName("EquipItem of row -1");
        yield return new TestCaseData(With(unequip, 2, 1, 0)).SetName("UnequipItem of slot 0");
        yield return new TestCaseData(With(unequip, 2, 1, 3)).SetName("UnequipItem of slot 3");
        yield return new TestCaseData(With(useItem, 2, 8, 0x00)).SetName("UseItem of row 0");
        yield return new TestCaseData(With(useItem, 2, 8, 0xFF)).SetName("UseItem of row -1");
        yield return new TestCaseData(With(buy, 2, 8, 0x00)).SetName("BuyItem from NPC 0");
        yield return new TestCaseData(With(buy, 18, 4, 0x00)).SetName("BuyItem of none");
        yield return new TestCaseData(With(sell, 10, 8, 0x00)).SetName("SellItem of row 0");
        yield return new TestCaseData(With(sell, 18, 4, 0x00)).SetName("SellItem of none");
    }

    [TestCaseSource(nameof(MalformedItemAndSkillCommands))]
    public void ItemOrSkillCommand_ThatIsMalformed_IsRejected(byte[] payload)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        AssertOnlyMalformed(queue, 1);
    }

    private static void AssertOnlyMalformed(InboundQueue queue, int expectedCount)
    {
        Assert.That(queue.Malformed, Is.EqualTo(expectedCount));
        Assert.That(queue.Count, Is.EqualTo(expectedCount));
        while (queue.TryDequeue(out InboundEvent inboundEvent))
        {
            Assert.That(inboundEvent.Kind, Is.EqualTo(InboundEventKind.Malformed));
        }
    }

    [Test]
    public void Fault_WhileHandlingOnePeer_ClosesThatPeerAndTheTickGoesOn()
    {
        var server = new TestServer();
        ConnectionId faulty = server.EnterWorld(1);
        ConnectionId healthy = server.EnterWorld(2);
        server.Transport.ClearSent();
        server.Transport.FailSendsTo.Add(faulty);

        // A living player's respawn is refused at once, from inside the handling of that peer's input; the answer to
        // the faulty peer is what fails. Database results have a fault boundary of their own and are not used here.
        server.SendRespawn(faulty, 1);
        server.SendRespawn(healthy, 1);
        server.Tick();

        Assert.That(server.Transport.Disconnects[faulty], Is.EqualTo(DisconnectReason.InternalError));
        Assert.That(server.Sessions.TryGet(faulty, out _), Is.False);
        Assert.That(
            server.Transport.ControlOpcodesSentTo(healthy),
            Does.Contain(MessageOpcode.CommandRejected),
            "the next peer's input was still handled in the same tick");
        Assert.That(server.Log.Entries.Count(entry => entry.Level == LogLevel.Error), Is.EqualTo(1));
        Assert.That(server.Log.Entries.Single(entry => entry.Level == LogLevel.Error).EventId.Name,
            Is.EqualTo("SessionFaulted"));
    }

    [Test]
    public void Hello_OfAnotherVersionOnAnotherChannel_IsMalformed()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Input, ForeignHello.Encode(ProtocolConstants.ProtocolVersion + 1, 40));

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void Hello_OfAnotherVersion_LongerAndInAnotherLayout_IsAHelloOfThatVersion()
    {
        InboundQueue queue = CreateQueue(16);
        int nextVersion = ProtocolConstants.ProtocolVersion + 1;

        queue.OnPayload(
            Peer,
            ProtocolChannel.Control,
            ForeignHello.Encode(nextVersion, ProtocolLimits.MaxClientPayloadBytes + 100));

        Assert.That(queue.TryDequeue(out InboundEvent decoded), Is.True);
        Assert.That(decoded.Kind, Is.EqualTo(InboundEventKind.Hello));
        Assert.That(decoded.Hello!.ProtocolVersion, Is.EqualTo(nextVersion));
        Assert.That(queue.Malformed, Is.Zero);
    }

    [Test]
    public void Hello_OfThisVersionInAnotherLayout_IsMalformed()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, ForeignHello.Encode(ProtocolConstants.ProtocolVersion, 40));

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void Inbound_WhenQueueFull_DropsAndCounts()
    {
        InboundQueue queue = CreateQueue(16);

        for (int index = 0; index < 20; index++)
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        }

        Assert.That(queue.Count, Is.EqualTo(16));
        Assert.That(queue.Dropped, Is.EqualTo(4));
    }

    [Test]
    public void Inbound_WhenQueueFull_StillAcceptsConnectionLifecycleEvents()
    {
        InboundQueue queue = CreateQueue(16);
        for (int index = 0; index < 16; index++)
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        }

        queue.OnDisconnected(Peer);
        queue.OnConnected(new ConnectionId(2));

        Assert.That(queue.Count, Is.EqualTo(18));
        Assert.That(queue.Dropped, Is.EqualTo(0));
    }

    [Test]
    public void Input_FromAConnectionTheServerNeverSaw_IsIgnored()
    {
        var server = new TestServer();

        server.SendHello(new ConnectionId(999));
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Sessions.Sessions, Is.Empty);
    }

    [Test]
    public void ItemAndSkillCommands_ThatAreWellFormed_AreQueuedAsCommands()
    {
        InboundQueue queue = CreateQueue(16);

        foreach (byte[] payload in new[]
                 {
                     UseSkillPayload(), EquipPayload(), UnequipPayload(), UseItemPayload(), BuyPayload(), SellPayload()
                 })
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, payload);
        }

        var kinds = new List<InboundEventKind>();
        while (queue.TryDequeue(out InboundEvent inboundEvent))
        {
            kinds.Add(inboundEvent.Kind);
        }

        Assert.That(queue.Malformed, Is.Zero);
        Assert.That(
            kinds,
            Is.EqualTo(
                new[]
                {
                    InboundEventKind.UseSkill, InboundEventKind.Equip, InboundEventKind.Unequip,
                    InboundEventKind.UseItem, InboundEventKind.Buy, InboundEventKind.Sell
                }));
        Assert.That(UseSkillPayload().Skip(4).Take(12), Is.EqualTo(Encoding.ASCII.GetBytes("skill.strike")));
    }

    [Test]
    public void MalformedInput_FromAPeer_IsCountedAndChangesNothing()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(3);
        server.Transport.ClearSent();

        server.Inbound.OnPayload(connection, ProtocolChannel.Control, new byte[] { 0xFF, 0xFF, 0x00 });
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.True);
    }

    [Test]
    public void Message_LargerThanAnyClientPayload_IsRejectedBeforeDecoding()
    {
        InboundQueue queue = CreateQueue(16);
        // A hello of this version: one of another version is read before the size check, to be told its mismatch.
        byte[] oversized = new byte[ProtocolLimits.MaxClientPayloadBytes + 1];
        oversized[0] = 0x01;
        oversized[2] = (byte)ProtocolConstants.ProtocolVersion;

        queue.OnPayload(Peer, ProtocolChannel.Control, oversized);

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void Message_OnWrongChannel_IsRejected()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Input, Hello());
        queue.OnPayload(Peer, ProtocolChannel.State, Hello());

        AssertOnlyMalformed(queue, 2);
    }

    [Test]
    public void Message_ThatOnlyAServerSends_IsRejected()
    {
        InboundQueue queue = CreateQueue(16);
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(despawn);

        queue.OnPayload(Peer, ProtocolChannel.Control, despawn);

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void OnPayload_ForWellFormedHello_EnqueuesADecodedEvent()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, Hello());

        Assert.That(queue.TryDequeue(out InboundEvent inboundEvent), Is.True);
        Assert.That(inboundEvent.Kind, Is.EqualTo(InboundEventKind.Hello));
        Assert.That(inboundEvent.Connection, Is.EqualTo(Peer));
        Assert.That(inboundEvent.Hello!.ClientBuildVersion, Is.EqualTo(ProtocolConstants.BuildVersion));
        Assert.That(queue.Malformed, Is.EqualTo(0));
    }

    [Test]
    public void TryDequeue_ReturnsEventsInArrivalOrderAndTracksCount()
    {
        InboundQueue queue = CreateQueue(16);
        queue.OnConnected(Peer);
        queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        queue.OnDisconnected(Peer);

        var kinds = new InboundEventKind[3];
        for (int index = 0; index < kinds.Length; index++)
        {
            queue.TryDequeue(out InboundEvent inboundEvent);
            kinds[index] = inboundEvent.Kind;
        }

        InboundEventKind[] expected =
            { InboundEventKind.Connected, InboundEventKind.Hello, InboundEventKind.Disconnected };
        Assert.That(kinds, Is.EqualTo(expected));
        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(queue.TryDequeue(out _), Is.False);
    }
}
}
