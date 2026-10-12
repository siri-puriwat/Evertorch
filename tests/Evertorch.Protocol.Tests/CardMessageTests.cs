using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 43, the cards (Network Protocol §6): an insertion from the bag, a removal at the Smith, and the Smith's
///     fees, pinned by their golden bytes, their largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class CardMessageTests
{
    // The card in row 5 into row 42, command 7.
    private static readonly byte[] InsertBytes =
    {
        0x2E, 0x00,
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x07, 0x00, 0x00, 0x00
    };

    // At the Smith 14, slot 2 of row 42, item.k, command 8.
    private static readonly byte[] RemoveBytes =
    {
        0x2F, 0x00,
        0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B,
        0x08, 0x00, 0x00, 0x00
    };

    // The Smith 14: item.b for 600 coins and item.k for 200.
    private static readonly byte[] FeesBytes =
    {
        0x2F, 0x80,
        0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x62, 0x58, 0x02, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B, 0xC8, 0x00, 0x00, 0x00
    };

    private static readonly ItemDefinitionId Keen = new("item.k");
    private static readonly ItemDefinitionId Bane = new("item.b");

    public static InsertCard InsertGolden => new(5, 42, 7);

    public static RemoveCard RemoveGolden => new(new EntityId(14), 42, 2, Keen, 8);

    public static CardRemovalFees FeesGolden =>
        new(new EntityId(14), new[] { new CardRemovalFee(Bane, 600), new CardRemovalFee(Keen, 200) });

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static ItemDefinitionId Longest(int index)
    {
        return new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}");
    }

    private static bool ReadAny(byte[] bytes)
    {
        return InsertCard.TryRead(bytes, out _) || RemoveCard.TryRead(bytes, out _)
            || CardRemovalFees.TryRead(bytes, out _);
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        Action noRow = () => _ = new InsertCard(0, 42, 1);
        Action noSlot = () => _ = new RemoveCard(new EntityId(14), 42, 0, Keen, 1);
        Action fifthSlot = () => _ = new RemoveCard(new EntityId(14), 42, 5, Keen, 1);
        Action noCard = () => _ = new RemoveCard(new EntityId(14), 42, 1, default, 1);
        Action dear = () => _ = new CardRemovalFee(Keen, ContentLimits.MaxCardRemovalFee + 1);
        Action none = () => _ = new CardRemovalFees(new EntityId(14), new CardRemovalFee[0]);
        Action twice = () => _ = new CardRemovalFees(
            new EntityId(14),
            new[] { new CardRemovalFee(Keen, 1), new CardRemovalFee(Keen, 2) });

        Assert.That(noRow, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noSlot, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(fifthSlot, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noCard, Throws.ArgumentException);
        Assert.That(dear, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(none, Throws.ArgumentException);
        Assert.That(twice, Throws.ArgumentException);
    }

    [Test]
    public void Messages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(InsertCard.EncodedLength, bytes => InsertGolden.Write(bytes)), Is.EqualTo(InsertBytes));
        Assert.That(
            Encode(RemoveGolden.GetEncodedLength(), bytes => RemoveGolden.Write(bytes)),
            Is.EqualTo(RemoveBytes));
        Assert.That(Encode(FeesGolden.GetEncodedLength(), bytes => FeesGolden.Write(bytes)), Is.EqualTo(FeesBytes));

        Assert.That(InsertCard.TryRead(InsertBytes, out InsertCard? insert), Is.True);
        Assert.That(
            (insert!.CardItem, insert.InventoryItem, insert.CommandSequence),
            Is.EqualTo((5L, 42L, 7u)));
        Assert.That(RemoveCard.TryRead(RemoveBytes, out RemoveCard? remove), Is.True);
        Assert.That(
            (remove!.Npc.Value, remove.InventoryItem, remove.Slot, remove.Card, remove.CommandSequence),
            Is.EqualTo((14L, 42L, (byte)2, Keen, 8u)));
        Assert.That(CardRemovalFees.TryRead(FeesBytes, out CardRemovalFees? fees), Is.True);
        Assert.That(
            fees!.Entries.Select(entry => (entry.Card, entry.Fee)),
            Is.EqualTo(new[] { (Bane, 600u), (Keen, 200u) }));
        foreach (byte[] golden in new[] { InsertBytes, RemoveBytes, FeesBytes })
        {
            WireMatrix.AssertRejectsEveryTruncation(golden, bytes => ReadAny(bytes));
            WireMatrix.AssertRejectsTrailingData(golden, bytes => ReadAny(bytes));
        }

        WireMatrix.AssertRejectsOtherOpcodes(InsertBytes, bytes => InsertCard.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(RemoveBytes, bytes => RemoveCard.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(FeesBytes, bytes => CardRemovalFees.TryRead(bytes, out _));
    }

    [Test]
    public void Readers_RefuseWhatBreaksARule()
    {
        Assert.That(InsertCard.TryRead(WireMatrix.With(InsertBytes, 2, 0x00), out _), Is.False, "a card's row of 0");
        Assert.That(InsertCard.TryRead(WireMatrix.With(InsertBytes, 10, 0x00), out _), Is.False, "an item's row of 0");
        Assert.That(RemoveCard.TryRead(WireMatrix.With(RemoveBytes, 2, 0x00), out _), Is.False, "an NPC of 0");
        Assert.That(RemoveCard.TryRead(WireMatrix.With(RemoveBytes, 10, 0x00), out _), Is.False, "a row of 0");
        Assert.That(RemoveCard.TryRead(WireMatrix.With(RemoveBytes, 18, 0x00), out _), Is.False, "slot 0");
        Assert.That(RemoveCard.TryRead(WireMatrix.With(RemoveBytes, 18, 0x05), out _), Is.False, "slot 5");
        Assert.That(RemoveCard.TryRead(WireMatrix.With(RemoveBytes, 21, (byte)'s'), out _), Is.False, "no item");
        Assert.That(CardRemovalFees.TryRead(WireMatrix.With(FeesBytes, 2, 0x00), out _), Is.False, "an NPC of 0");
        Assert.That(CardRemovalFees.TryRead(WireMatrix.With(FeesBytes, 10, 0x00), out _), Is.False, "no card");
        Assert.That(CardRemovalFees.TryRead(WireMatrix.With(FeesBytes, 10, 0x0F), out _), Is.False, "fifteen");
        Assert.That(
            CardRemovalFees.TryRead(WireMatrix.With(FeesBytes, 19, 0x41, 0x42, 0x0F, 0x00), out _),
            Is.False,
            "a fee above 1,000,000");
        Assert.That(
            CardRemovalFees.TryRead(WireMatrix.With(FeesBytes, 30, (byte)'b'), out _),
            Is.False,
            "a card listed twice");
    }

    // Network Protocol §6: an insertion is 22 bytes, a removal at most 89, and the fees 11 and 70 an entry, 431 for six
    // and 991 for the fourteen content may hold.
    [Test]
    public void TheLargestMessages_KeepTheirSizes()
    {
        var remove = new RemoveCard(new EntityId(long.MaxValue), long.MaxValue, 4, Longest(0), uint.MaxValue);
        CardRemovalFee[] fourteen = Enumerable.Range(0, CardRemovalFees.MaxEntries)
            .Select(index => new CardRemovalFee(Longest(index), ContentLimits.MaxCardRemovalFee))
            .ToArray();

        Assert.That(InsertCard.EncodedLength, Is.EqualTo(22));
        Assert.That((remove.GetEncodedLength(), RemoveCard.MaxEncodedLength), Is.EqualTo((89, 89)));
        Assert.That(
            new CardRemovalFees(new EntityId(1), fourteen.Take(6).ToArray()).GetEncodedLength(),
            Is.EqualTo(431));
        Assert.That(new CardRemovalFees(new EntityId(1), fourteen).GetEncodedLength(), Is.EqualTo(991));
        byte[] written = Encode(
            new CardRemovalFees(new EntityId(1), fourteen).GetEncodedLength(),
            bytes => new CardRemovalFees(new EntityId(1), fourteen).Write(bytes));
        Assert.That(CardRemovalFees.TryRead(written, out CardRemovalFees? read), Is.True);
        Assert.That(read!.Entries, Has.Count.EqualTo(14));
    }
}
}
