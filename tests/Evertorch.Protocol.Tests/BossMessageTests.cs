using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     <see cref="BossAnnouncement" /> (Network Protocol §6, §9): the boss's appearance with no name, and its fall with
///     its most valuable player's name or none, at most 94 bytes.
/// </summary>
[TestFixture]
public sealed class BossMessageTests
{
    private static readonly byte[] AppearedBytes =
    {
        0x24, 0x80,
        0x01,
        0x15, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x6D, 0x6F,
        0x6E, 0x61, 0x72, 0x63, 0x68,
        0x00, 0x00
    };

    private static readonly byte[] FellBytes =
    {
        0x24, 0x80,
        0x02,
        0x15, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x6D, 0x6F,
        0x6E, 0x61, 0x72, 0x63, 0x68,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61
    };

    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

    public static BossAnnouncement AppearedGolden => new(BossAnnouncementKind.Appeared, Monarch, string.Empty);

    public static BossAnnouncement FellGolden => new(BossAnnouncementKind.Fell, Monarch, "Anna");

    private static byte[] Encode(BossAnnouncement message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        int written = message.Write(bytes);
        Assert.That(written, Is.EqualTo(bytes.Length));
        return bytes;
    }

    [TestCase((byte)0)]
    [TestCase((byte)3)]
    public void BossAnnouncement_OfAnUnknownKind_IsMalformed(byte kind)
    {
        Assert.That(BossAnnouncement.TryRead(WireMatrix.With(AppearedBytes, 2, kind), out _), Is.False);
    }

    [Test]
    public void BossAnnouncement_AFallWithNobodyToName_ReadsAnEmptyName()
    {
        byte[] bytes = Encode(new BossAnnouncement(BossAnnouncementKind.Fell, Monarch, string.Empty));

        Assert.That(BossAnnouncement.TryRead(bytes, out BossAnnouncement? message), Is.True);
        Assert.That((message!.Kind, message.Name), Is.EqualTo((BossAnnouncementKind.Fell, string.Empty)));
    }

    // The longest monster ID and the longest name (Network Protocol §6).
    [Test]
    public void BossAnnouncement_AtEveryLimit_IsNinetyFourBytes()
    {
        var longest = new MonsterDefinitionId("monster." + new string('m', DefinitionIdLimits.MaxLength - 8));
        var message =
            new BossAnnouncement(BossAnnouncementKind.Fell, longest, new string('A', CharacterNames.MaxLength));

        Assert.That(message.GetEncodedLength(), Is.EqualTo(94));
        Assert.That(BossAnnouncement.TryRead(Encode(message), out _), Is.True);
    }

    [Test]
    public void BossAnnouncement_CutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(FellBytes, bytes => BossAnnouncement.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(FellBytes, bytes => BossAnnouncement.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(FellBytes, bytes => BossAnnouncement.TryRead(bytes, out _));
    }

    [Test]
    public void BossAnnouncement_NamingSomeoneOnAnAppearance_OrABrokenName_IsMalformed()
    {
        byte[] namedAppearance = WireMatrix.With(FellBytes, 2, 0x01);
        byte[] brokenName = WireMatrix.With(FellBytes, 29, 0x20);

        Assert.That(BossAnnouncement.TryRead(namedAppearance, out _), Is.False, "an appearance names nobody");
        Assert.That(BossAnnouncement.TryRead(brokenName, out _), Is.False, "\"A na\" breaks the name rule");
    }

    [Test]
    public void BossAnnouncement_OfAnotherKindOfDefinition_IsMalformed()
    {
        byte[] renamed = WireMatrix.With(AppearedBytes, 5, 0x6E);

        Assert.That(BossAnnouncement.TryRead(renamed, out _), Is.False, "\"nonster.slime_monarch\" is no monster ID");
    }

    [Test]
    public void BossAnnouncement_WithoutAMonster_CannotBeMade()
    {
        Action create = () => _ = new BossAnnouncement(BossAnnouncementKind.Appeared, default, string.Empty);

        Assert.That(create, Throws.ArgumentException);
    }

    [Test]
    public void BossAnnouncement_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(AppearedGolden), Is.EqualTo(AppearedBytes));
        Assert.That(Encode(FellGolden), Is.EqualTo(FellBytes));
        Assert.That(BossAnnouncement.TryRead(AppearedBytes, out BossAnnouncement? appeared), Is.True);
        Assert.That(BossAnnouncement.TryRead(FellBytes, out BossAnnouncement? fell), Is.True);
        Assert.That(
            (appeared!.Kind, appeared.Monster, appeared.Name),
            Is.EqualTo((BossAnnouncementKind.Appeared, Monarch, string.Empty)));
        Assert.That((fell!.Kind, fell.Monster, fell.Name), Is.EqualTo((BossAnnouncementKind.Fell, Monarch, "Anna")));
    }
}
}
