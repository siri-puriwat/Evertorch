using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class CharacterSheetTests
{
    // Job level 3 with 20 of 80 job experience; 7 stat points and 2 skill points; STR 5 (2), AGI 11 (3), VIT 99 (at
    // the cap, 0), INT 5 (2), DEX 21 (4), LUK 1 (2); attack 46, magic attack 12, defense 5, magic defense 6, hit 188,
    // flee 125, critical 13 (1.3 %), attack speed 154.
    private static readonly byte[] SheetBytes =
    {
        0x1F, 0x80, 0x03,
        0x14, 0, 0, 0, 0, 0, 0, 0,
        0x50, 0, 0, 0, 0, 0, 0, 0,
        0x07, 0x00, 0x02,
        0x05, 0x02, 0x0B, 0x03, 0x63, 0x00, 0x05, 0x02, 0x15, 0x04, 0x01, 0x02,
        0x2E, 0x00, 0x0C, 0x00, 0x05, 0x00, 0x06, 0x00, 0xBC, 0x00, 0x7D, 0x00, 0x0D, 0x00, 0x9A, 0x00
    };

    // Offsets into the golden bytes.
    private const int JobLevel = 2;
    private const int FirstStat = 22;
    private const int VitCost = 27;

    public static CharacterSheet Golden =>
        new(
            3,
            20,
            80,
            7,
            2,
            new[]
            {
                new CharacterSheetStat(5, 2), new CharacterSheetStat(11, 3), new CharacterSheetStat(99, 0),
                new CharacterSheetStat(5, 2), new CharacterSheetStat(21, 4), new CharacterSheetStat(1, 2)
            },
            46,
            12,
            5,
            6,
            188,
            125,
            13,
            154);

    [TestCase(JobLevel, new byte[] { 0x00 }, "job level 0")]
    [TestCase(FirstStat, new byte[] { 0x64, 0x00 }, "a statistic above 99")]
    [TestCase(FirstStat, new byte[] { 0x05, 0x00 }, "no cost below the cap")]
    [TestCase(VitCost, new byte[] { 0x02 }, "a cost at the cap")]
    public void CharacterSheet_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        byte[] bytes = WireMatrix.With(SheetBytes, offset, replacement);

        Assert.That(CharacterSheet.TryRead(bytes, out _), Is.False, reason);
    }

    [Test]
    public void CharacterSheet_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[CharacterSheet.EncodedLength];
        int length = Golden.Write(written);
        bool isRead = CharacterSheet.TryRead(SheetBytes, out CharacterSheet? read);

        Assert.That(CharacterSheet.EncodedLength, Is.EqualTo(50));
        Assert.That(length, Is.EqualTo(SheetBytes.Length));
        Assert.That(written, Is.EqualTo(SheetBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read, Is.EqualTo(Golden));
        Assert.That(
            (read!.JobLevel, read.JobExperience, read.JobExperienceToNextLevel, read.StatPoints, read.SkillPoints),
            Is.EqualTo(((byte)3, 20ul, 80ul, (ushort)7, (byte)2)));
        Assert.That(
            read.Stats.Select(stat => (stat.Value, stat.NextCost)),
            Is.EqualTo(new[] { (5, 2), (11, 3), (99, 0), (5, 2), (21, 4), (1, 2) }.Select(pair =>
                ((byte)pair.Item1, (byte)pair.Item2))));
        Assert.That(
            (read.Attack, read.MagicAttack, read.Defense, read.MagicDefense, read.Hit, read.Flee, read.Critical,
                read.AttackSpeed),
            Is.EqualTo(((ushort)46, (ushort)12, (ushort)5, (ushort)6, (ushort)188, (ushort)125, (ushort)13,
                (ushort)154)));
        WireMatrix.AssertRejectsEveryTruncation(SheetBytes, bytes => CharacterSheet.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(SheetBytes, bytes => CharacterSheet.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(SheetBytes, bytes => CharacterSheet.TryRead(bytes, out _));
    }

    [Test]
    public void CharacterSheet_ThatDiffersInOneField_IsNotEqual()
    {
        CharacterSheet golden = Golden;
        var faster = new CharacterSheet(3, 20, 80, 7, 2, golden.Stats, 46, 12, 5, 6, 188, 125, 13, 155);

        Assert.That(Golden, Is.EqualTo(golden));
        Assert.That(faster, Is.Not.EqualTo(golden));
        Assert.That(golden.Equals(null), Is.False);
    }

    [Test]
    public void CharacterSheet_WithoutSixStatistics_CannotBeBuilt()
    {
        Action five = () => _ = new CharacterSheet(1, 0, 0, 0, 0, new CharacterSheetStat[5], 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(five, Throws.ArgumentException);
    }
}
}
