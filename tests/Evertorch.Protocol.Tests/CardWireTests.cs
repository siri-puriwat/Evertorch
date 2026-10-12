using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 42, the rows' cards (Network Protocol §6): a count and each card's ID after a row's refine level in
///     every message that carries rows, the readers' rules, and the parts bounded by bytes, so a bag of 100 rows with
///     four of the longest cards a row still travels in messages of at most 1,020 bytes.
/// </summary>
[TestFixture]
public sealed class CardWireTests
{
    private static readonly ItemDefinitionId Sword = new("item.a");
    private static readonly ItemDefinitionId Keen = new("item.k");
    private static readonly ItemDefinitionId Bane = new("item.b");

    // From revision 7 to 8 with 250 coins: row 11, item.a x 1, refined to +7, holding item.k then item.b.
    private static readonly byte[] CardedChangeBytes =
    {
        0x10, 0x80, 0x07, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0xFA, 0x00, 0x00, 0x00, 0x01,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x07,
        0x02,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x62
    };

    // The offsets of the row's quantity and its card count.
    private const int Quantity = 31;
    private const int CardCount = 37;

    public static StorageChanged CardedStorageChanged =>
        new(3, 4, new StorageEntry(9, Sword, 1, 7, new[] { Keen, Bane }));

    public static TradeSide CardedSide =>
        new(TradeSideOwner.Partner, true, false, 100, new[] { new TradeEntry(0, Sword, 1, 4, new[] { Keen }) });

    private static ItemDefinitionId LongestCard(int index)
    {
        return new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}");
    }

    private static ItemDefinitionId[] FourLongestCards()
    {
        return Enumerable.Range(0, ProtocolLimits.MaxCardsPerItem).Select(LongestCard).ToArray();
    }

    private static ItemDefinitionId LongestItem()
    {
        return new ItemDefinitionId($"item.{new string('i', DefinitionIdLimits.MaxLength - 5)}");
    }

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Encode(InventoryChanged message)
    {
        return Encode(message.GetEncodedLength(), bytes => message.Write(bytes));
    }

    // The largest row: the longest item ID and four of the longest cards, 345 bytes (Network Protocol §6).
    [Test]
    public void ABagOfAHundredCardedRows_TravelsInPartsOfAt1020Bytes_AndReassembles()
    {
        InventoryEntry[] rows = Enumerable.Range(1, 100)
            .Select(id => new InventoryEntry(id, LongestItem(), 1, EquipmentSlot.None, 10, FourLongestCards()))
            .ToArray();

        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(9, 1_000_000_000, rows);

        var reassembled = new List<InventoryEntry>();
        foreach (InventorySnapshot part in parts)
        {
            byte[] bytes = Encode(part.GetEncodedLength(), written => part.Write(written));
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxReliableMessageBytes));
            Assert.That(InventorySnapshot.TryRead(bytes, out InventorySnapshot? read), Is.True);
            reassembled.AddRange(read!.Entries);
        }

        Assert.That(new InventorySnapshot(9, 0, 0, 1, new[] { rows[0] }).GetEncodedLength(), Is.EqualTo(13 + 345));
        Assert.That(parts, Has.Count.EqualTo(50), "two rows a part");
        Assert.That(parts.Select(part => (int)part.Part), Is.EqualTo(Enumerable.Range(0, 50)));
        Assert.That(parts.All(part => part.PartCount == 50) && parts[49].IsLast, Is.True);
        Assert.That(reassembled.Select(entry => entry.InventoryItem),
            Is.EqualTo(rows.Select(row => row.InventoryItem)));
        Assert.That(reassembled.All(entry => entry.Cards.SequenceEqual(FourLongestCards())), Is.True);
    }

    [Test]
    public void ACardedRow_RoundTripsThroughStorageAndATrade()
    {
        byte[] stored = Encode(CardedStorageChanged.GetEncodedLength(), bytes => CardedStorageChanged.Write(bytes));
        byte[] side = Encode(CardedSide.GetEncodedLength(), bytes => CardedSide.Write(bytes));

        Assert.That(StorageChanged.TryRead(stored, out StorageChanged? changed), Is.True);
        Assert.That(TradeSide.TryRead(side, out TradeSide? traded), Is.True);
        Assert.That((changed!.Row.RefineLevel, changed.Row.Cards), Is.EqualTo(((byte)7, new[] { Keen, Bane })));
        Assert.That(
            (traded!.Entries.Single().RefineLevel, traded.Entries.Single().Cards),
            Is.EqualTo(((byte)4, new[] { Keen })));
    }

    // An answer reports at most six rows (Network Protocol §6), but a change of twelve carded rows cannot be built, and
    // no reader reads past 1,020 bytes.
    [Test]
    public void AChangeOrAPart_PastOneReliableMessage_CannotBeBuiltOrRead()
    {
        InventoryEntry[] rows = Enumerable.Range(1, 3)
            .Select(id => new InventoryEntry(id, LongestItem(), 1, EquipmentSlot.None, 0, FourLongestCards()))
            .ToArray();
        byte[] twoRows = Encode(new InventoryChanged(1, 2, 0, rows.Take(2).ToArray()));
        byte[] past = twoRows.Concat(new byte[ProtocolLimits.MaxReliableMessageBytes]).ToArray();
        past[14] = 3;

        Action change = () => _ = new InventoryChanged(1, 2, 0, rows);
        Action part = () => _ = new InventorySnapshot(1, 0, 0, 1, rows);

        Assert.That(change, Throws.ArgumentException);
        Assert.That(part, Throws.ArgumentException);
        Assert.That(InventoryChanged.TryRead(past, out _), Is.False);
        Assert.That(InventoryChanged.TryRead(twoRows, out InventoryChanged? read), Is.True);
        Assert.That(read!.Changes, Has.Count.EqualTo(2));
    }

    [Test]
    public void ASideOfTenCardedRows_TravelsInParts_AndReassembles()
    {
        TradeEntry[] rows = Enumerable.Range(1, TradeSide.MaxEntries)
            .Select(id => new TradeEntry(0, LongestItem(), 1, 10, FourLongestCards()))
            .ToArray();

        IReadOnlyList<TradeSide> parts =
            TradeSide.CreateParts(TradeSideOwner.Partner, true, true, ContentLimits.MaxCurrency, rows);

        var reassembled = new List<TradeEntry>();
        foreach (TradeSide part in parts)
        {
            byte[] bytes = Encode(part.GetEncodedLength(), written => part.Write(written));
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxReliableMessageBytes));
            Assert.That(TradeSide.TryRead(bytes, out TradeSide? read), Is.True);
            Assert.That(
                (read!.Owner, read.IsLocked, read.IsConfirmed, read.Coins, read.PartCount),
                Is.EqualTo((TradeSideOwner.Partner, true, true, ContentLimits.MaxCurrency, (byte)parts.Count)));
            reassembled.AddRange(read.Entries);
        }

        Assert.That(parts, Has.Count.EqualTo(5), "two rows a part");
        Assert.That(reassembled, Has.Count.EqualTo(10));
        Assert.That(reassembled.All(entry => entry.Cards.SequenceEqual(FourLongestCards())), Is.True);
        Assert.That(
            TradeSide.CreateParts(TradeSideOwner.Own, false, false, 0, Array.Empty<TradeEntry>()).Single().IsLast,
            Is.True,
            "a side without rows is one empty part");
    }

    [Test]
    public void AStorageOfCardedRows_TravelsInPartsOfAt1020Bytes_AndReassembles()
    {
        StorageEntry[] rows = Enumerable.Range(1, 30)
            .Select(id => new StorageEntry(id, LongestItem(), 1, 10, FourLongestCards()))
            .ToArray();

        IReadOnlyList<StorageSnapshot> parts = StorageSnapshot.CreateParts(3, ContentLimits.MaxDepositFee, rows);

        var reassembled = new List<StorageEntry>();
        foreach (StorageSnapshot part in parts)
        {
            byte[] bytes = Encode(part.GetEncodedLength(), written => part.Write(written));
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxReliableMessageBytes));
            Assert.That(StorageSnapshot.TryRead(bytes, out StorageSnapshot? read), Is.True);
            reassembled.AddRange(read!.Entries);
        }

        Assert.That(parts, Has.Count.EqualTo(15));
        Assert.That(reassembled.Select(entry => entry.StorageItem), Is.EqualTo(rows.Select(row => row.StorageItem)));
        Assert.That(reassembled.All(entry => entry.Cards.SequenceEqual(FourLongestCards())), Is.True);
    }

    [Test]
    public void Cards_OnARowOtherThanOfOne_OrPastFour_OrNotAnItem_AreMalformed()
    {
        byte[] stack = WireMatrix.With(CardedChangeBytes, Quantity, 0x02);
        byte[] removed = WireMatrix.With(CardedChangeBytes, Quantity, 0x00);
        byte[] five = WireMatrix.With(CardedChangeBytes, CardCount, 0x05);
        byte[] skill = WireMatrix.With(CardedChangeBytes, CardCount + 3, (byte)'s');
        byte[] none = WireMatrix.With(CardedChangeBytes, CardCount, 0x00);

        Assert.That(InventoryChanged.TryRead(stack, out _), Is.False, "cards on a stack");
        Assert.That(InventoryChanged.TryRead(removed, out _), Is.False, "cards on a removed row");
        Assert.That(InventoryChanged.TryRead(five, out _), Is.False, "five cards");
        Assert.That(InventoryChanged.TryRead(skill, out _), Is.False, "a card that is no item");
        Assert.That(InventoryChanged.TryRead(none, out _), Is.False, "a count of none before two cards");
    }

    [Test]
    public void Changed_OfACardedRow_MatchesItsGoldenBytes_AndReadsTheCardsInSlotOrder()
    {
        var message = new InventoryChanged(
            7,
            8,
            250,
            new[] { new InventoryEntry(11, Sword, 1, EquipmentSlot.None, 7, new[] { Keen, Bane }) });

        bool isRead = InventoryChanged.TryRead(CardedChangeBytes, out InventoryChanged? read);

        Assert.That(Encode(message), Is.EqualTo(CardedChangeBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Changes.Single().Cards, Is.EqualTo(new[] { Keen, Bane }));
        Assert.That(read.Changes.Single().RefineLevel, Is.EqualTo((byte)7));
        WireMatrix.AssertRejectsEveryTruncation(CardedChangeBytes, bytes => InventoryChanged.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(CardedChangeBytes, bytes => InventoryChanged.TryRead(bytes, out _));
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse_AndADefaultRowHasNoCard()
    {
        ItemDefinitionId[] five = Enumerable.Repeat(Keen, 5).ToArray();
        Action pastFour = () => _ = new InventoryEntry(1, Sword, 1, EquipmentSlot.None, 0, five);
        Action onAStack = () => _ = new StorageEntry(1, Sword, 2, 0, new[] { Keen });
        Action onNothing = () => _ = new InventoryEntry(1, Sword, 0, EquipmentSlot.None, 0, new[] { Keen });
        Action onATradedStack = () => _ = new TradeEntry(1, Sword, 3, 0, new[] { Keen });

        Assert.That(pastFour, Throws.ArgumentException);
        Assert.That(onAStack, Throws.ArgumentException);
        Assert.That(onNothing, Throws.ArgumentException);
        Assert.That(onATradedStack, Throws.ArgumentException);
        Assert.That(
            (default(InventoryEntry).Cards, default(StorageEntry).Cards, default(TradeEntry).Cards),
            Is.EqualTo((Array.Empty<ItemDefinitionId>(), Array.Empty<ItemDefinitionId>(),
                Array.Empty<ItemDefinitionId>())));
    }

    [Test]
    public void TwelveRowsWithoutACard_StillMakeOnePart_OfNineHundredEightyFiveBytes()
    {
        InventoryEntry[] rows = Enumerable.Range(1, 12)
            .Select(id => new InventoryEntry(id, LongestItem(), uint.MaxValue))
            .ToArray();
        StorageEntry[] stored = Enumerable.Range(1, 13).Select(id => new StorageEntry(id, LongestItem(), 1)).ToArray();

        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(1, 0, rows);
        IReadOnlyList<StorageSnapshot> storage = StorageSnapshot.CreateParts(1, 0, stored);

        Assert.That((parts.Count, parts[0].GetEncodedLength()), Is.EqualTo((1, 985)));
        Assert.That(storage.Select(part => part.Entries.Count), Is.EqualTo(new[] { 12, 1 }), "twelve rows a part");
    }
}
}
