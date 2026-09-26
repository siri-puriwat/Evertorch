using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the quest's messages, so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedQuestMessageTests
{
    // quest.a from NPC 14, command sequence 0x0A0B0C0D.
    private static readonly byte[] AcceptBytes =
    {
        0x15, 0x00, 0x0E, 0, 0, 0, 0, 0, 0, 0,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x61,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    // quest.a to NPC 14, command sequence 0x0A0B0C0D.
    private static readonly byte[] CompleteBytes =
    {
        0x16, 0x00, 0x0E, 0, 0, 0, 0, 0, 0, 0,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x61,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    // quest.a active at 3 of 5, and quest.b completed at 5 of 5.
    private static readonly byte[] LogBytes =
    {
        0x1E, 0x80, 0x02,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x61, 0x01, 0x03, 0x00, 0x05, 0x00,
        0x07, 0x00, 0x71, 0x75, 0x65, 0x73, 0x74, 0x2E, 0x62, 0x02, 0x05, 0x00, 0x05, 0x00
    };

    [Test]
    public void AcceptQuest_WriteAndRead_MatchGoldenBytes()
    {
        var message = new AcceptQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 0x0A0B0C0D);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = AcceptQuest.TryRead(AcceptBytes, out AcceptQuest? read);

        Assert.That(buffer, Is.EqualTo(AcceptBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Npc, Is.EqualTo(new EntityId(14)));
        Assert.That(read.Quest.Value, Is.EqualTo("quest.a"));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }

    [Test]
    public void CompleteQuest_WriteAndRead_MatchGoldenBytes()
    {
        var message = new CompleteQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 0x0A0B0C0D);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = CompleteQuest.TryRead(CompleteBytes, out CompleteQuest? read);

        Assert.That(buffer, Is.EqualTo(CompleteBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Quest.Value, Is.EqualTo("quest.a"));
    }

    [Test]
    public void QuestLog_WriteAndRead_MatchGoldenBytes()
    {
        var message = new QuestLog(
            new[]
            {
                new QuestLogEntry(new QuestDefinitionId("quest.a"), QuestState.Active, 3, 5),
                new QuestLogEntry(new QuestDefinitionId("quest.b"), QuestState.Completed, 5, 5)
            });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = QuestLog.TryRead(LogBytes, out QuestLog? read);

        Assert.That(buffer, Is.EqualTo(LogBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Entries.Count, Is.EqualTo(2));
        Assert.That(read.Entries[0].State, Is.EqualTo(QuestState.Active));
        Assert.That(read.Entries[0].Progress, Is.EqualTo((ushort)3));
        Assert.That(read.Entries[1].State, Is.EqualTo(QuestState.Completed));
        Assert.That(read.Entries[1].Count, Is.EqualTo((ushort)5));
    }

    [Test]
    public void QuestMessages_RouteOnTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[]
                 {
                     MessageOpcode.AcceptQuest, MessageOpcode.CompleteQuest, MessageOpcode.QuestLog
                 })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }
}
}
