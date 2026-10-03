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

    private static byte[] AcceptQuestPayload()
    {
        var message = new AcceptQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    private static byte[] CompleteQuestPayload()
    {
        var message = new CompleteQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    private static byte[] ResetBuildPayload()
    {
        byte[] payload = new byte[ResetBuild.EncodedLength];
        new ResetBuild(new EntityId(15), 9).Write(payload);
        return payload;
    }

    private static byte[] ChangeJobPayload()
    {
        var message = new ChangeJob(new EntityId(15), new JobDefinitionId("job.vanguard"), 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    private static byte[] ChatPayload(ChatChannel channel, string recipient, string text)
    {
        var message = new ChatSend(channel, recipient, text, 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    // A chat line cut short, one byte too long, on a channel no client sends, naming someone for a line said nearby,
    // or carrying a control character where the text rule allows none (Network Protocol §11).
    private static IEnumerable<TestCaseData> MalformedChat()
    {
        byte[] nearby = ChatPayload(ChatChannel.Nearby, string.Empty, "hi");
        byte[] whisper = ChatPayload(ChatChannel.Whisper, "Tester8", "hi");
        yield return new TestCaseData(nearby.Take(nearby.Length - 1).ToArray()).SetName("ChatSend cut short");
        yield return new TestCaseData(nearby.Concat(new byte[] { 0 }).ToArray()).SetName("ChatSend too long");
        yield return new TestCaseData(With(nearby, 2, 1, 0x04)).SetName("ChatSend as a whisper sent");
        yield return new TestCaseData(With(whisper, 2, 1, 0x01)).SetName("ChatSend nearby naming a recipient");
        yield return new TestCaseData(With(nearby, 7, 1, 0x0A)).SetName("ChatSend with a line break");
        yield return new TestCaseData(With(nearby, 7, 1, 0xC3)).SetName("ChatSend of broken UTF-8");
    }

    [TestCaseSource(nameof(MalformedChat))]
    public void Chat_ThatIsMalformed_IsRejected(byte[] payload)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        AssertOnlyMalformed(queue, 1);
    }

    private static byte[] Payload(int length, Func<byte[], int> write)
    {
        byte[] payload = new byte[length];
        write(payload);
        return payload;
    }

    private static IEnumerable<TestCaseData> PartyCommands()
    {
        var invite = new PartyInvite("Tester8", 9);
        var reply = new PartyReply("Tester7", true, 9);
        var kick = new PartyKick("Tester8", 9);
        var lead = new PartyLead("Tester8", 9);
        yield return new TestCaseData(
            Payload(invite.GetEncodedLength(), bytes => invite.Write(bytes)),
            InboundEventKind.PartyInvite,
            "Tester8",
            false).SetName("PartyInvite");
        yield return new TestCaseData(
            Payload(reply.GetEncodedLength(), bytes => reply.Write(bytes)),
            InboundEventKind.PartyReply,
            "Tester7",
            true).SetName("PartyReply");
        yield return new TestCaseData(
            Payload(PartyLeave.EncodedLength, bytes => new PartyLeave(9).Write(bytes)),
            InboundEventKind.PartyLeave,
            string.Empty,
            false).SetName("PartyLeave");
        yield return new TestCaseData(
            Payload(kick.GetEncodedLength(), bytes => kick.Write(bytes)),
            InboundEventKind.PartyKick,
            "Tester8",
            false).SetName("PartyKick");
        yield return new TestCaseData(
            Payload(lead.GetEncodedLength(), bytes => lead.Write(bytes)),
            InboundEventKind.PartyLead,
            "Tester8",
            false).SetName("PartyLead");
    }

    [TestCaseSource(nameof(PartyCommands))]
    public void PartyCommand_ThatIsWellFormed_IsQueuedWithItsNameAnswerAndSequence(
        byte[] payload,
        InboundEventKind kind,
        string name,
        bool isAccepted)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        Assert.That(queue.TryDequeue(out InboundEvent command), Is.True);
        Assert.That(
            (command.Kind, command.Name, command.IsAccepted, command.CommandSequence),
            Is.EqualTo((kind, name, isAccepted, 9u)));
    }

    [TestCaseSource(nameof(PartyCommands))]
    public void PartyCommand_CutShort_IsRejected(byte[] payload, InboundEventKind kind, string name, bool isAccepted)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload.Take(payload.Length - 1).ToArray());

        AssertOnlyMalformed(queue, 1);
    }

    private static IEnumerable<TestCaseData> TradeCommands()
    {
        var request = new TradeRequest("Tester8", 9);
        var reply = new TradeReply("Tester7", true, 9);
        yield return new TestCaseData(
            Payload(request.GetEncodedLength(), bytes => request.Write(bytes)),
            InboundEventKind.TradeRequest,
            "Tester8",
            false,
            0L,
            0u).SetName("TradeRequest");
        yield return new TestCaseData(
            Payload(reply.GetEncodedLength(), bytes => reply.Write(bytes)),
            InboundEventKind.TradeReply,
            "Tester7",
            true,
            0L,
            0u).SetName("TradeReply");
        yield return new TestCaseData(
            Payload(TradeOffer.EncodedLength, bytes => new TradeOffer(42, 5, 9).Write(bytes)),
            InboundEventKind.TradeOffer,
            string.Empty,
            false,
            42L,
            5u).SetName("TradeOffer");
        yield return new TestCaseData(
            Payload(TradeLock.EncodedLength, bytes => new TradeLock(9).Write(bytes)),
            InboundEventKind.TradeLock,
            string.Empty,
            false,
            0L,
            0u).SetName("TradeLock");
        yield return new TestCaseData(
            Payload(TradeConfirm.EncodedLength, bytes => new TradeConfirm(9).Write(bytes)),
            InboundEventKind.TradeConfirm,
            string.Empty,
            false,
            0L,
            0u).SetName("TradeConfirm");
        yield return new TestCaseData(
            Payload(TradeCancel.EncodedLength, bytes => new TradeCancel(9).Write(bytes)),
            InboundEventKind.TradeCancel,
            string.Empty,
            false,
            0L,
            0u).SetName("TradeCancel");
    }

    private static IEnumerable<TestCaseData> StorageCommands()
    {
        yield return new TestCaseData(
            Payload(StorageOpen.EncodedLength, bytes => new StorageOpen(new EntityId(40), 9).Write(bytes)),
            InboundEventKind.StorageOpen,
            0L,
            0u).SetName("StorageOpen");
        yield return new TestCaseData(
            Payload(StorageDeposit.EncodedLength, bytes => new StorageDeposit(new EntityId(40), 42, 5, 9).Write(bytes)),
            InboundEventKind.StorageDeposit,
            42L,
            5u).SetName("StorageDeposit");
        yield return new TestCaseData(
            Payload(StorageWithdraw.EncodedLength,
                bytes => new StorageWithdraw(new EntityId(40), 7, 2, 9).Write(bytes)),
            InboundEventKind.StorageWithdraw,
            7L,
            2u).SetName("StorageWithdraw");
    }

    [TestCaseSource(nameof(StorageCommands))]
    public void StorageCommand_ThatIsWellFormed_IsQueuedWithTheNpcTheRowAndItsSequence(
        byte[] payload,
        InboundEventKind kind,
        long row,
        uint quantity)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        Assert.That(queue.TryDequeue(out InboundEvent command), Is.True);
        Assert.That(
            (command.Kind, command.Target, command.InventoryItem, command.Quantity, command.CommandSequence),
            Is.EqualTo((kind, new EntityId(40), row, quantity, 9u)));
    }

    [TestCaseSource(nameof(StorageCommands))]
    public void StorageCommand_CutShort_IsRejected(byte[] payload, InboundEventKind kind, long row, uint quantity)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload.Take(payload.Length - 1).ToArray());

        AssertOnlyMalformed(queue, 1);
    }

    [TestCaseSource(nameof(TradeCommands))]
    public void TradeCommand_ThatIsWellFormed_IsQueuedWithWhatItNamesAndItsSequence(
        byte[] payload,
        InboundEventKind kind,
        string name,
        bool isAccepted,
        long row,
        uint quantity)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        Assert.That(queue.TryDequeue(out InboundEvent command), Is.True);
        Assert.That(
            (command.Kind, command.Name, command.IsAccepted, command.InventoryItem, command.Quantity,
                command.CommandSequence),
            Is.EqualTo((kind, name, isAccepted, row, quantity, 9u)));
    }

    [TestCaseSource(nameof(TradeCommands))]
    public void TradeCommand_CutShort_IsRejected(
        byte[] payload,
        InboundEventKind kind,
        string name,
        bool isAccepted,
        long row,
        uint quantity)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload.Take(payload.Length - 1).ToArray());

        AssertOnlyMalformed(queue, 1);
    }

    private static byte[] LearnSkillPayload()
    {
        var message = new LearnSkill(new SkillDefinitionId("skill.strike"), 9);
        byte[] payload = new byte[message.GetEncodedLength()];
        message.Write(payload);
        return payload;
    }

    private static byte[] AllocateStatPayload()
    {
        byte[] payload = new byte[AllocateStat.EncodedLength];
        new AllocateStat(PrimaryStat.Agi, 2, 9).Write(payload);
        return payload;
    }

    private static byte[] With(byte[] payload, int start, int count, byte value)
    {
        byte[] changed = (byte[])payload.Clone();
        changed.AsSpan(start, count).Fill(value);
        return changed;
    }

    // Milestone 6's, 7's, and 9's commands cut short, one byte too long, or carrying a value no client may send
    // (Network Protocol §11). The item row sits after the opcode, the slot, the NPC, and the statistic too, the skill
    // ID's text after its two-byte length, and the quest ID's after the NPC and its length.
    private static IEnumerable<TestCaseData> MalformedItemAndSkillCommands()
    {
        byte[] useSkill = UseSkillPayload();
        byte[] equip = EquipPayload();
        byte[] unequip = UnequipPayload();
        byte[] useItem = UseItemPayload();
        byte[] buy = BuyPayload();
        byte[] sell = SellPayload();
        byte[] accept = AcceptQuestPayload();
        byte[] complete = CompleteQuestPayload();
        byte[] allocate = AllocateStatPayload();
        byte[] learn = LearnSkillPayload();
        byte[] reset = ResetBuildPayload();
        byte[] change = ChangeJobPayload();
        foreach ((string name, byte[] payload) in new[]
                 {
                     ("UseSkill", useSkill), ("EquipItem", equip), ("UnequipItem", unequip), ("UseItem", useItem),
                     ("BuyItem", buy), ("SellItem", sell), ("AcceptQuest", accept), ("CompleteQuest", complete),
                     ("AllocateStat", allocate), ("LearnSkill", learn), ("ResetBuild", reset), ("ChangeJob", change)
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
        const int quest = 12;
        yield return new TestCaseData(With(accept, 2, 8, 0x00)).SetName("AcceptQuest from NPC 0");
        yield return new TestCaseData(With(accept, quest + 5, 1, 0x20)).SetName("AcceptQuest naming no quest");
        yield return new TestCaseData(With(complete, 2, 8, 0x00)).SetName("CompleteQuest at NPC 0");
        yield return new TestCaseData(With(complete, quest, 1, 0xFF)).SetName("CompleteQuest of broken UTF-8");
        yield return new TestCaseData(With(complete, quest - 2, 2, 0xFF)).SetName("CompleteQuest longer than any ID");
        yield return new TestCaseData(With(allocate, 2, 1, 0)).SetName("AllocateStat of statistic 0");
        yield return new TestCaseData(With(allocate, 2, 1, 7)).SetName("AllocateStat of statistic 7");
        yield return new TestCaseData(With(allocate, 3, 1, 0)).SetName("AllocateStat of no steps");
        yield return new TestCaseData(With(learn, text, 1, 0x20)).SetName("LearnSkill naming no skill");
        yield return new TestCaseData(With(learn, text, 1, 0xFF)).SetName("LearnSkill of broken UTF-8");
        yield return new TestCaseData(With(learn, text - 2, 2, 0xFF)).SetName("LearnSkill longer than any ID");
        yield return new TestCaseData(With(reset, 2, 8, 0x00)).SetName("ResetBuild at NPC 0");
        yield return new TestCaseData(With(reset, 2, 8, 0xFF)).SetName("ResetBuild at NPC -1");
        const int job = 12;
        yield return new TestCaseData(With(change, 2, 8, 0x00)).SetName("ChangeJob at NPC 0");
        yield return new TestCaseData(With(change, job, 1, 0x20)).SetName("ChangeJob naming no job");
        yield return new TestCaseData(With(change, job, 1, 0xFF)).SetName("ChangeJob of broken UTF-8");
        yield return new TestCaseData(With(change, job - 2, 2, 0xFF)).SetName("ChangeJob longer than any ID");
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
    public void Chat_ThatIsWellFormed_IsQueuedWithItsChannelRecipientTextAndSequence()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, ChatPayload(ChatChannel.Whisper, "Tester8", "hi there"));

        Assert.That(queue.TryDequeue(out InboundEvent chat), Is.True);
        Assert.That(
            (chat.Kind, chat.Channel, chat.Name, chat.Text, chat.CommandSequence),
            Is.EqualTo((InboundEventKind.Chat, ChatChannel.Whisper, "Tester8", "hi there", 9u)));
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
                     UseSkillPayload(), EquipPayload(), UnequipPayload(), UseItemPayload(), BuyPayload(), SellPayload(),
                     AcceptQuestPayload(), CompleteQuestPayload(), LearnSkillPayload(), ResetBuildPayload(),
                     ChangeJobPayload(), AllocateStatPayload()
                 })
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, payload);
        }

        var kinds = new List<InboundEventKind>();
        InboundEvent allocate = default;
        InboundEvent learn = default;
        InboundEvent reset = default;
        InboundEvent change = default;
        while (queue.TryDequeue(out InboundEvent inboundEvent))
        {
            kinds.Add(inboundEvent.Kind);
            learn = inboundEvent.Kind == InboundEventKind.LearnSkill ? inboundEvent : learn;
            reset = inboundEvent.Kind == InboundEventKind.ResetBuild ? inboundEvent : reset;
            change = inboundEvent.Kind == InboundEventKind.ChangeJob ? inboundEvent : change;
            allocate = inboundEvent;
        }

        Assert.That(queue.Malformed, Is.Zero);
        Assert.That(
            kinds,
            Is.EqualTo(
                new[]
                {
                    InboundEventKind.UseSkill, InboundEventKind.Equip, InboundEventKind.Unequip,
                    InboundEventKind.UseItem, InboundEventKind.Buy, InboundEventKind.Sell, InboundEventKind.AcceptQuest,
                    InboundEventKind.CompleteQuest, InboundEventKind.LearnSkill, InboundEventKind.ResetBuild,
                    InboundEventKind.ChangeJob, InboundEventKind.AllocateStat
                }));
        Assert.That(
            (allocate.Stat, allocate.Quantity, allocate.CommandSequence),
            Is.EqualTo((PrimaryStat.Agi, 2u, 9u)),
            "the statistic and its steps");
        Assert.That((learn.Skill, learn.CommandSequence), Is.EqualTo((new SkillDefinitionId("skill.strike"), 9u)));
        Assert.That((reset.Target, reset.CommandSequence), Is.EqualTo((new EntityId(15), 9u)));
        Assert.That(
            (change.Target, change.Job, change.CommandSequence),
            Is.EqualTo((new EntityId(15), new JobDefinitionId("job.vanguard"), 9u)));
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
