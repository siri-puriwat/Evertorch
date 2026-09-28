using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class SkillMessageTests
{
    private static readonly byte[] SkillA = { 0x07, 0x00, 0x73, 0x6B, 0x69, 0x6C, 0x6C, 0x2E, 0x61 };

    private static readonly byte[] UseSkillBytes = Concat(
        new byte[] { 0x08, 0x00 },
        SkillA,
        new byte[] { 0x2A, 0, 0, 0, 0, 0, 0, 0, 0x78, 0x56, 0x34, 0x12 });

    private static readonly byte[] CastStartedBytes = Concat(
        new byte[] { 0x0B, 0x80, 0x01, 0, 0, 0, 0, 0, 0, 0 },
        SkillA,
        new byte[] { 0x2A, 0, 0, 0, 0, 0, 0, 0, 0x10, 0, 0, 0, 0x33, 0x05, 0, 0 });

    private static readonly byte[] ResolvedBytes = Concat(
        new byte[] { 0x0C, 0x80, 0x01, 0, 0, 0, 0, 0, 0, 0, 0x2A, 0, 0, 0, 0, 0, 0, 0 },
        SkillA,
        new byte[] { 0x01, 0x11, 0, 0, 0, 0x20, 0, 0, 0, 0x94, 0x02 });

    // skill.a at level 2 of 5, with no prerequisite.
    private static readonly byte[] ListBytes = Concat(
        new byte[] { 0x1B, 0x80, 0x01 },
        SkillA,
        new byte[]
        {
            0x00, 0x00, 0xC0, 0x3F, 0x08, 0, 0, 0, 0xD0, 0x07, 0, 0, 0xF4, 0x01, 0, 0, 0xE2, 0x04, 0, 0,
            0x02, 0x05, 0xFF, 0x00
        });

    // skill.a (level 1 of 5) and skill.b (level 0 of 3), which requires skill.a at level 2.
    private static readonly byte[] TreeBytes = Concat(
        new byte[] { 0x1B, 0x80, 0x02 },
        SkillA,
        new byte[] { 0x00, 0x00, 0xC0, 0x3F, 0x08, 0, 0, 0, 0xD0, 0x07, 0, 0, 0xF4, 0x01, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0x01, 0x05, 0xFF, 0x00 },
        new byte[] { 0x07, 0x00, 0x73, 0x6B, 0x69, 0x6C, 0x6C, 0x2E, 0x62 },
        new byte[] { 0, 0, 0, 0, 0x0F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0x00, 0x03, 0x00, 0x02 });

    // Offsets into TreeBytes: the first entry's level bytes, then the second's.
    private const int FirstLevel = 32;
    private const int SecondLevel = 65;

    private static readonly byte[] LearnBytes =
        Concat(new byte[] { 0x18, 0x00 }, SkillA, new byte[] { 0x78, 0x56, 0x34, 0x12 });

    private static readonly SkillDefinitionId Skill = new("skill.a");

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(part => part).ToArray();
    }

    private static byte[] Write(int length, Func<byte[], int> write)
    {
        byte[] buffer = new byte[length];
        Assert.That(write(buffer), Is.EqualTo(length));
        return buffer;
    }

    private static void AssertStrict(byte[] golden, Func<byte[], bool> tryRead)
    {
        Assert.That(tryRead(golden), Is.True);
        WireMatrix.AssertRejectsEveryTruncation(golden, tryRead);
        WireMatrix.AssertRejectsTrailingData(golden, tryRead);
        WireMatrix.AssertRejectsOtherOpcodes(golden, tryRead);
    }

    [TestCase(10, (byte)0x00, "no target")]
    [TestCase(27, (byte)0x00, "outcome 0")]
    [TestCase(27, (byte)0x06, "an outcome not yet defined")]
    public void SkillResolved_WithAnImpossibleField_IsRefused(int offset, byte value, string why)
    {
        Assert.That(SkillResolved.TryRead(WireMatrix.With(ResolvedBytes, offset, value), out _), Is.False, why);
    }

    [TestCase(SecondLevel, (byte)4, "a level above the maximum")]
    [TestCase(SecondLevel + 1, (byte)0, "a maximum of 0")]
    [TestCase(SecondLevel + 1, (byte)6, "a maximum above 5")]
    [TestCase(SecondLevel + 2, (byte)2, "an index not below the count")]
    [TestCase(SecondLevel + 2, (byte)1, "the skill itself")]
    [TestCase(SecondLevel + 3, (byte)6, "a prerequisite level above that skill's maximum")]
    [TestCase(SecondLevel + 3, (byte)0, "a prerequisite at level 0")]
    [TestCase(FirstLevel + 3, (byte)1, "a level without a prerequisite")]
    public void SkillList_WithAnImpossibleLevelOrPrerequisite_IsRefused(int offset, byte value, string why)
    {
        Assert.That(SkillList.TryRead(TreeBytes, out _), Is.True, "the unchanged tree reads");
        Assert.That(SkillList.TryRead(WireMatrix.With(TreeBytes, offset, value), out _), Is.False, why);
    }

    [Test]
    public void LearnSkill_ForGoldenBytes_RoundTrips_AndRefusesAStringThatIsNoSkill()
    {
        var message = new LearnSkill(Skill, 0x12345678);

        byte[] written = Write(message.GetEncodedLength(), buffer => message.Write(buffer));
        bool isRead = LearnSkill.TryRead(LearnBytes, out LearnSkill? read);

        Assert.That(written, Is.EqualTo(LearnBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.Skill, read.CommandSequence), Is.EqualTo((Skill, 0x12345678u)));
        AssertStrict(LearnBytes, bytes => LearnSkill.TryRead(bytes, out _));
        Assert.That(LearnSkill.TryRead(WireMatrix.With(LearnBytes, 4, 0x20), out _), Is.False, "a space");
        Assert.That(LearnSkill.TryRead(WireMatrix.With(LearnBytes, 4, 0xFF), out _), Is.False, "broken UTF-8");
    }

    [Test]
    public void SkillCastStarted_ForGoldenBytes_RoundTrips_AndRefusesNoCaster()
    {
        var message = new SkillCastStarted(new EntityId(1), Skill, new EntityId(42), 16, 1331);

        byte[] written = Write(message.GetEncodedLength(), buffer => message.Write(buffer));
        bool isRead = SkillCastStarted.TryRead(CastStartedBytes, out SkillCastStarted? read);

        Assert.That(written, Is.EqualTo(CastStartedBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.Caster, read.Skill, read.Target, read.StartTick, read.CastMs),
            Is.EqualTo((new EntityId(1), Skill, new EntityId(42), 16u, 1331u)));
        AssertStrict(CastStartedBytes, bytes => SkillCastStarted.TryRead(bytes, out _));
        Assert.That(SkillCastStarted.TryRead(WireMatrix.With(CastStartedBytes, 2, 0), out _), Is.False);
    }

    [Test]
    public void SkillList_ForGoldenBytes_RoundTrips()
    {
        var message = new SkillList(
            new[] { new SkillListEntry(Skill, 1.5f, 8, 2000, 500, 1250, 2, 5, SkillListEntry.NoPrerequisite, 0) });

        byte[] written = Write(message.GetEncodedLength(), buffer => message.Write(buffer));
        bool isRead = SkillList.TryRead(ListBytes, out SkillList? read);

        Assert.That(written, Is.EqualTo(ListBytes));
        Assert.That(isRead, Is.True);
        SkillListEntry entry = read!.Skills.Single();
        Assert.That((entry.Skill, entry.Range, entry.SpCost, entry.CooldownMs, entry.AfterCastDelayMs,
            entry.RemainingCooldownMs), Is.EqualTo((Skill, 1.5f, 8u, 2000u, 500u, 1250u)));
        Assert.That(
            (entry.Level, entry.MaxLevel, entry.PrerequisiteIndex, entry.PrerequisiteLevel, entry.IsLearned),
            Is.EqualTo(((byte)2, (byte)5, SkillListEntry.NoPrerequisite, (byte)0, true)));
        AssertStrict(ListBytes, bytes => SkillList.TryRead(bytes, out _));
    }

    [Test]
    public void SkillList_OfATree_ReadsEachPrerequisiteByItsIndex()
    {
        bool isRead = SkillList.TryRead(TreeBytes, out SkillList? read);

        Assert.That(isRead, Is.True);
        SkillListEntry second = read!.Skills[1];
        Assert.That(second.Skill, Is.EqualTo(new SkillDefinitionId("skill.b")));
        Assert.That(
            (second.Level, second.MaxLevel, second.PrerequisiteIndex, second.PrerequisiteLevel, second.IsLearned),
            Is.EqualTo(((byte)0, (byte)3, (byte)0, (byte)2, false)));
        AssertStrict(TreeBytes, bytes => SkillList.TryRead(bytes, out _));
    }

    [Test]
    public void SkillList_WithMoreLeftThanTheCooldownOrANegativeRange_IsRefused()
    {
        byte[] moreLeft = WireMatrix.With(ListBytes, 28, 0xD1, 0x07, 0, 0);
        byte[] negativeRange = WireMatrix.With(ListBytes, 12, 0x00, 0x00, 0xC0, 0xBF);

        Assert.That(SkillList.TryRead(moreLeft, out _), Is.False);
        Assert.That(SkillList.TryRead(negativeRange, out _), Is.False);
    }

    [Test]
    public void SkillList_WithTheMostEntriesAndTheLongestIds_FitsOneReliableMessage()
    {
        SkillListEntry[] entries = Enumerable.Range(0, SkillList.MaxEntries)
            .Select(index => new SkillListEntry(
                new SkillDefinitionId($"skill.{(char)('a' + index)}{new string('x', 57)}"),
                6f,
                uint.MaxValue,
                uint.MaxValue,
                uint.MaxValue,
                uint.MaxValue,
                5,
                5,
                (byte)(index == 0 ? SkillListEntry.NoPrerequisite : 0),
                (byte)(index == 0 ? 0 : 5)))
            .ToArray();
        var message = new SkillList(entries);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        Assert.That(SkillList.TryRead(buffer, out SkillList? read), Is.True);
        Assert.That(read!.Skills, Has.Count.EqualTo(SkillList.MaxEntries));
        Assert.That(buffer.Length, Is.EqualTo(993), "under LiteNetLib's 1,020 bytes a reliable message may carry");
    }

    [Test]
    public void SkillList_WithTheSameSkillTwiceOrTooManyEntries_IsRefused()
    {
        byte[] entry = ListBytes.Skip(3).ToArray();
        byte[] twice = Concat(new byte[] { 0x1B, 0x80, 0x02 }, entry, entry);
        byte[] tooMany = WireMatrix.With(ListBytes, 2, SkillList.MaxEntries + 1);

        Assert.That(SkillList.TryRead(twice, out _), Is.False);
        Assert.That(SkillList.TryRead(tooMany, out _), Is.False);
    }

    [Test]
    public void SkillOutcome_Values_KeepTheirWireNumbers()
    {
        Assert.That(
            Array.ConvertAll((SkillOutcome[])Enum.GetValues(typeof(SkillOutcome)), outcome => (byte)outcome),
            Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5 }));
        Assert.That((byte)SkillOutcome.Healed, Is.EqualTo(4));
        Assert.That((byte)SkillOutcome.Applied, Is.EqualTo(5));
    }

    [Test]
    public void SkillResolved_AboveAFullHealthBar_IsRefused()
    {
        byte[] aboveFull = WireMatrix.With(ResolvedBytes, 36, 0xE9, 0x03);

        Assert.That(SkillResolved.TryRead(aboveFull, out _), Is.False);
    }

    [Test]
    public void SkillResolved_ForGoldenBytes_RoundTrips()
    {
        var message = new SkillResolved(new EntityId(1), new EntityId(42), Skill, SkillOutcome.Hit, 17, 32, 660);

        byte[] written = Write(message.GetEncodedLength(), buffer => message.Write(buffer));
        bool isRead = SkillResolved.TryRead(ResolvedBytes, out SkillResolved? read);

        Assert.That(written, Is.EqualTo(ResolvedBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.Caster, read.Target, read.Skill, read.Outcome, read.Amount, read.ServerTick),
            Is.EqualTo((new EntityId(1), new EntityId(42), Skill, SkillOutcome.Hit, 17u, 32u)));
        Assert.That(read.TargetHealthPermille, Is.EqualTo(660));
        AssertStrict(ResolvedBytes, bytes => SkillResolved.TryRead(bytes, out _));
    }

    [Test]
    public void UseSkill_ForGoldenBytes_RoundTrips()
    {
        var message = new UseSkill(Skill, new EntityId(42), 0x12345678);

        byte[] written = Write(message.GetEncodedLength(), buffer => message.Write(buffer));
        bool isRead = UseSkill.TryRead(UseSkillBytes, out UseSkill? read);

        Assert.That(written, Is.EqualTo(UseSkillBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.Skill, read.Target, read.CommandSequence),
            Is.EqualTo((Skill, new EntityId(42), 0x12345678u)));
        AssertStrict(UseSkillBytes, bytes => UseSkill.TryRead(bytes, out _));
    }

    [Test]
    public void UseSkill_ForTheCasterItselfOrAnotherKindOfId_ReadsOrRefuses()
    {
        byte[] onItself = WireMatrix.With(UseSkillBytes, 11, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] itemId = WireMatrix.With(UseSkillBytes, 4, 0x69, 0x74, 0x65, 0x6D, 0x73);

        Assert.That(UseSkill.TryRead(onItself, out UseSkill? self), Is.True, "target 0 is the caster");
        Assert.That(self!.Target, Is.EqualTo(default(EntityId)));
        Assert.That(UseSkill.TryRead(itemId, out _), Is.False);
    }
}
}
