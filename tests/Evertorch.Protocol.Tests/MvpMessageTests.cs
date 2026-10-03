using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     <see cref="MvpAwarded" /> (Network Protocol §6, §9): the boss, the MVP experience gained, and the prize with its
///     amount and place, or no prize, at most 147 bytes; the item, the amount, and the place agree on whether there is
///     a prize.
/// </summary>
[TestFixture]
public sealed class MvpMessageTests
{
    private static readonly byte[] InTheBagBytes =
    {
        0x25, 0x80,
        0x15, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x6D, 0x6F,
        0x6E, 0x61, 0x72, 0x63, 0x68,
        0xB8, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x19, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x72, 0x6D, 0x6F, 0x72, 0x2E, 0x6D, 0x6F, 0x6E, 0x61, 0x72,
        0x63, 0x68, 0x5F, 0x6D, 0x61, 0x6E, 0x74, 0x6C, 0x65,
        0x01, 0x00, 0x00, 0x00,
        0x01
    };

    private static readonly byte[] NoPrizeBytes =
    {
        0x25, 0x80,
        0x15, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x6D, 0x6F,
        0x6E, 0x61, 0x72, 0x63, 0x68,
        0xB8, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00
    };

    private const int AmountAt = 60;
    private const int PlacedAt = 64;
    private const int NoPrizeAmountAt = 35;
    private const int NoPrizePlacedAt = 39;

    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly ItemDefinitionId Mantle = new("item.armor.monarch_mantle");

    public static MvpAwarded InTheBagGolden => new(Monarch, 3000, Mantle, 1, PrizePlacement.Bag);

    public static MvpAwarded NoPrizeGolden => new(Monarch, 3000, null, 0, PrizePlacement.None);

    private static byte[] Encode(MvpAwarded message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        int written = message.Write(bytes);
        Assert.That(written, Is.EqualTo(bytes.Length));
        return bytes;
    }

    [TestCase((byte)3)]
    [TestCase((byte)255)]
    public void MvpAwarded_OfAnUnknownPlace_IsMalformed(byte placed)
    {
        Assert.That(MvpAwarded.TryRead(WireMatrix.With(InTheBagBytes, PlacedAt, placed), out _), Is.False);
    }

    // The longest monster ID and the longest item ID (Network Protocol §6).
    [Test]
    public void MvpAwarded_AtEveryLimit_IsOneHundredFortySevenBytes()
    {
        var monster = new MonsterDefinitionId("monster." + new string('m', DefinitionIdLimits.MaxLength - 8));
        var item = new ItemDefinitionId("item." + new string('i', DefinitionIdLimits.MaxLength - 5));
        var message = new MvpAwarded(monster, ulong.MaxValue, item, uint.MaxValue, PrizePlacement.Feet);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(147));
        Assert.That(MvpAwarded.TryRead(Encode(message), out _), Is.True);
    }

    [Test]
    public void MvpAwarded_AtTheFeet_ReadsItsPlace()
    {
        byte[] bytes = Encode(new MvpAwarded(Monarch, 0, Mantle, 1, PrizePlacement.Feet));

        Assert.That(MvpAwarded.TryRead(bytes, out MvpAwarded? message), Is.True);
        Assert.That((message!.MvpExperience, message.Placed), Is.EqualTo((0UL, PrizePlacement.Feet)));
    }

    [Test]
    public void MvpAwarded_CutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(InTheBagBytes, bytes => MvpAwarded.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(InTheBagBytes, bytes => MvpAwarded.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(InTheBagBytes, bytes => MvpAwarded.TryRead(bytes, out _));
    }

    [Test]
    public void MvpAwarded_OfAnotherKindOfDefinition_IsMalformed()
    {
        byte[] notAMonster = WireMatrix.With(InTheBagBytes, 4, 0x6E);
        byte[] notAnItem = WireMatrix.With(InTheBagBytes, 35, 0x6A);

        Assert.That(MvpAwarded.TryRead(notAMonster, out _), Is.False, "\"nonster.slime_monarch\" is no monster ID");
        Assert.That(MvpAwarded.TryRead(notAnItem, out _), Is.False, "\"jtem.armor.monarch_mantle\" is no item ID");
    }

    [Test]
    public void MvpAwarded_WhoseItemAmountAndPlaceDisagree_IsMalformed()
    {
        Assert.That(
            MvpAwarded.TryRead(WireMatrix.With(InTheBagBytes, AmountAt, 0x00), out _),
            Is.False,
            "a prize of no amount");
        Assert.That(
            MvpAwarded.TryRead(WireMatrix.With(InTheBagBytes, PlacedAt, 0x00), out _),
            Is.False,
            "a prize put nowhere");
        Assert.That(
            MvpAwarded.TryRead(WireMatrix.With(NoPrizeBytes, NoPrizeAmountAt, 0x01), out _),
            Is.False,
            "an amount of no prize");
        Assert.That(
            MvpAwarded.TryRead(WireMatrix.With(NoPrizeBytes, NoPrizePlacedAt, 0x01), out _),
            Is.False,
            "no prize, in the bag");
    }

    [Test]
    public void MvpAwarded_WithoutAMonster_OrWhoseItemAmountAndPlaceDisagree_CannotBeMade()
    {
        Action noMonster = () => _ = new MvpAwarded(default, 0, null, 0, PrizePlacement.None);
        Action noAmount = () => _ = new MvpAwarded(Monarch, 0, Mantle, 0, PrizePlacement.Bag);
        Action nowhere = () => _ = new MvpAwarded(Monarch, 0, Mantle, 1, PrizePlacement.None);
        Action nothingPlaced = () => _ = new MvpAwarded(Monarch, 0, null, 0, PrizePlacement.Feet);

        Assert.That(noMonster, Throws.ArgumentException);
        Assert.That(noAmount, Throws.ArgumentException);
        Assert.That(nowhere, Throws.ArgumentException);
        Assert.That(nothingPlaced, Throws.ArgumentException);
    }

    [Test]
    public void MvpAwarded_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(InTheBagGolden), Is.EqualTo(InTheBagBytes));
        Assert.That(Encode(NoPrizeGolden), Is.EqualTo(NoPrizeBytes));
        Assert.That(MvpAwarded.TryRead(InTheBagBytes, out MvpAwarded? inTheBag), Is.True);
        Assert.That(MvpAwarded.TryRead(NoPrizeBytes, out MvpAwarded? noPrize), Is.True);
        Assert.That(
            (inTheBag!.Monster, inTheBag.MvpExperience, inTheBag.Item, inTheBag.Amount, inTheBag.Placed),
            Is.EqualTo((Monarch, 3000UL, (ItemDefinitionId?)Mantle, 1U, PrizePlacement.Bag)));
        Assert.That(
            (noPrize!.Item, noPrize.Amount, noPrize.Placed),
            Is.EqualTo(((ItemDefinitionId?)null, 0U, PrizePlacement.None)));
    }
}
}
