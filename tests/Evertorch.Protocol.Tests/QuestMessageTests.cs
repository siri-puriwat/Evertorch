using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class QuestMessageTests
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

    private static bool ReadAccept(byte[] bytes)
    {
        return AcceptQuest.TryRead(bytes, out _);
    }

    private static bool ReadComplete(byte[] bytes)
    {
        return CompleteQuest.TryRead(bytes, out _);
    }

    private static bool ReadLog(byte[] bytes)
    {
        return QuestLog.TryRead(bytes, out _);
    }

    // The first fourteen entries as a log, the fifteenth written after them, and the count raised to fifteen: all a
    // writer without the limit would send.
    private static byte[] FifteenEntries(QuestLogEntry[] fifteen)
    {
        var head = new QuestLog(fifteen.Take(QuestLog.MaxEntries).ToArray());
        var tail = new QuestLog(new[] { fifteen[QuestLog.MaxEntries] });
        byte[] headBytes = new byte[head.GetEncodedLength()];
        byte[] tailBytes = new byte[tail.GetEncodedLength()];
        head.Write(headBytes);
        tail.Write(tailBytes);
        byte[] bytes = headBytes.Concat(tailBytes.Skip(3)).ToArray();
        bytes[2] = QuestLog.MaxEntries + 1;
        return bytes;
    }

    [TestCase(2, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "NPC 0")]
    [TestCase(9, new byte[] { 0xFF }, "a negative NPC")]
    [TestCase(12, new byte[] { 0x6A }, "'juest.a' for a quest")]
    public void AcceptQuest_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        Assert.That(ReadAccept(WireMatrix.With(AcceptBytes, offset, replacement)), Is.False, reason);
    }

    [TestCase(2, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "NPC 0")]
    [TestCase(9, new byte[] { 0xFF }, "a negative NPC")]
    [TestCase(12, new byte[] { 0x6A }, "'juest.a' for a quest")]
    public void CompleteQuest_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        Assert.That(ReadComplete(WireMatrix.With(CompleteBytes, offset, replacement)), Is.False, reason);
    }

    [TestCase(5, new byte[] { 0x6A }, "'juest.a' for a quest")]
    [TestCase(12, new byte[] { 0x00 }, "state 0")]
    [TestCase(12, new byte[] { 0x03 }, "state 3")]
    [TestCase(13, new byte[] { 0x06, 0x00 }, "progress past the count")]
    [TestCase(15, new byte[] { 0x00, 0x00 }, "a count of 0")]
    [TestCase(25, new byte[] { 0x61 }, "the same quest twice")]
    [TestCase(27, new byte[] { 0x04, 0x00 }, "a completed quest short of its count")]
    public void QuestLog_WithAnImpossibleEntry_IsRefused(int offset, byte[] replacement, string reason)
    {
        Assert.That(ReadLog(WireMatrix.With(LogBytes, offset, replacement)), Is.False, reason);
    }

    [Test]
    public void AcceptQuest_ForGoldenBytes_RoundTrips()
    {
        var message = new AcceptQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 0x0A0B0C0D);
        byte[] written = new byte[message.GetEncodedLength()];
        int length = message.Write(written);

        bool isRead = AcceptQuest.TryRead(AcceptBytes, out AcceptQuest? read);

        Assert.That(length, Is.EqualTo(23));
        Assert.That(written, Is.EqualTo(AcceptBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read!.Npc, read.Quest.Value, read.CommandSequence),
            Is.EqualTo((new EntityId(14), "quest.a", 0x0A0B0C0Du)));
        WireMatrix.AssertRejectsEveryTruncation(AcceptBytes, ReadAccept);
        WireMatrix.AssertRejectsTrailingData(AcceptBytes, ReadAccept);
        WireMatrix.AssertRejectsOtherOpcodes(AcceptBytes, ReadAccept);
    }

    [Test]
    public void CompleteQuest_ForGoldenBytes_RoundTrips()
    {
        var message = new CompleteQuest(new EntityId(14), new QuestDefinitionId("quest.a"), 0x0A0B0C0D);
        byte[] written = new byte[message.GetEncodedLength()];
        int length = message.Write(written);

        bool isRead = CompleteQuest.TryRead(CompleteBytes, out CompleteQuest? read);

        Assert.That(length, Is.EqualTo(23));
        Assert.That(written, Is.EqualTo(CompleteBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read!.Npc, read.Quest.Value, read.CommandSequence),
            Is.EqualTo((new EntityId(14), "quest.a", 0x0A0B0C0Du)));
        WireMatrix.AssertRejectsEveryTruncation(CompleteBytes, ReadComplete);
        WireMatrix.AssertRejectsTrailingData(CompleteBytes, ReadComplete);
        WireMatrix.AssertRejectsOtherOpcodes(CompleteBytes, ReadComplete);
    }

    [Test]
    public void QuestCommands_AtTheirLargest_Are80Bytes_AndWithoutAQuest_CannotBeBuilt()
    {
        var quest = new QuestDefinitionId($"quest.{new string('a', 58)}");
        Action accept = () => _ = new AcceptQuest(new EntityId(14), default, 1);
        Action complete = () => _ = new CompleteQuest(new EntityId(14), default, 1);

        Assert.That(
            new AcceptQuest(new EntityId(long.MaxValue), quest, uint.MaxValue).GetEncodedLength(),
            Is.EqualTo(80));
        Assert.That(
            new CompleteQuest(new EntityId(long.MaxValue), quest, uint.MaxValue).GetEncodedLength(),
            Is.EqualTo(80));
        Assert.That(accept, Throws.InstanceOf<ArgumentException>());
        Assert.That(complete, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void QuestLog_AtItsLargest_Is997Bytes_AndPastIt_CannotBeBuiltOrRead()
    {
        QuestLogEntry[] entries = Enumerable.Range(0, QuestLog.MaxEntries)
            .Select(index => new QuestLogEntry(
                new QuestDefinitionId($"quest.{(char)('a' + index)}{new string('a', 57)}"),
                QuestState.Active,
                ushort.MaxValue - 1,
                ushort.MaxValue))
            .ToArray();
        var largest = new QuestLog(entries);
        QuestLogEntry[] fifteen = Enumerable.Range(0, QuestLog.MaxEntries + 1)
            .Select(index => new QuestLogEntry(
                new QuestDefinitionId($"quest.{(char)('a' + index)}"),
                QuestState.Active,
                0,
                1))
            .ToArray();
        Action tooMany = () => _ = new QuestLog(entries.Append(entries[0]).ToArray());

        Assert.That(largest.GetEncodedLength(), Is.EqualTo(997));
        Assert.That(ReadLog(FifteenEntries(fifteen)), Is.False, "fifteen entries");
        Assert.That(tooMany, Throws.ArgumentException);
    }

    [Test]
    public void QuestLog_Empty_IsThreeBytes_AndReadsBack()
    {
        byte[] written = new byte[3];
        int length = new QuestLog(Array.Empty<QuestLogEntry>()).Write(written);

        Assert.That((length, written), Is.EqualTo((3, new byte[] { 0x1E, 0x80, 0x00 })));
        Assert.That(QuestLog.TryRead(written, out QuestLog? read) && read!.Entries.Count == 0, Is.True);
    }

    [Test]
    public void QuestLog_ForGoldenBytes_RoundTrips()
    {
        var message = new QuestLog(
            new[]
            {
                new QuestLogEntry(new QuestDefinitionId("quest.a"), QuestState.Active, 3, 5),
                new QuestLogEntry(new QuestDefinitionId("quest.b"), QuestState.Completed, 5, 5)
            });
        byte[] written = new byte[message.GetEncodedLength()];
        int length = message.Write(written);

        bool isRead = QuestLog.TryRead(LogBytes, out QuestLog? read);

        Assert.That(length, Is.EqualTo(31));
        Assert.That(written, Is.EqualTo(LogBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            read!.Entries.Select(entry => (entry.Quest.Value, entry.State, entry.Progress, entry.Count)),
            Is.EqualTo(
                new[]
                {
                    ("quest.a", QuestState.Active, (ushort)3, (ushort)5),
                    ("quest.b", QuestState.Completed, (ushort)5, (ushort)5)
                }));
        WireMatrix.AssertRejectsEveryTruncation(LogBytes, ReadLog);
        WireMatrix.AssertRejectsTrailingData(LogBytes, ReadLog);
        WireMatrix.AssertRejectsOtherOpcodes(LogBytes, ReadLog);
    }
}
}
