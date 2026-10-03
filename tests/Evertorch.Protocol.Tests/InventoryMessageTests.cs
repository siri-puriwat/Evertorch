using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class InventoryMessageTests
{
    private static readonly byte[] ResyncBytes = { 0x0F, 0x00 };

    // Revision 7 and 250 coins, part 1 of 3: row 11, item.a x 2, worn as the weapon.
    private static readonly byte[] SnapshotBytes =
    {
        0x0F, 0x80, 0x07, 0x00, 0x00, 0x00, 0xFA, 0x00, 0x00, 0x00, 0x01, 0x03, 0x01,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x02, 0x00, 0x00, 0x00,
        0x01, 0x00
    };

    // From revision 7 to 8 with 250 coins: row 11, item.a, removed.
    private static readonly byte[] ChangedBytes =
    {
        0x10, 0x80, 0x07, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0xFA, 0x00, 0x00, 0x00, 0x01,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00
    };

    // From revision 7 to 8, where only the coins moved, to 250.
    private static readonly byte[] CoinsOnlyBytes =
    {
        0x10, 0x80, 0x07, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0xFA, 0x00, 0x00, 0x00, 0x00
    };

    // 1,000,000,001, one above the cap, little-endian.
    private static readonly byte[] AboveTheCap = { 0x01, 0xCA, 0x9A, 0x3B };

    private const int SnapshotCoins = 6;
    private const int ChangedCoins = 10;

    private static readonly ItemDefinitionId ItemA = new("item.a");

    private static bool ReadResync(byte[] bytes)
    {
        return InventoryResyncRequest.TryRead(bytes, out _);
    }

    private static bool ReadSnapshot(byte[] bytes)
    {
        return InventorySnapshot.TryRead(bytes, out _);
    }

    private static bool ReadChanged(byte[] bytes)
    {
        return InventoryChanged.TryRead(bytes, out _);
    }

    private static byte[] Encode(InventorySnapshot message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static InventoryEntry LargestEntry(long id)
    {
        return new InventoryEntry(id, new ItemDefinitionId($"item.{new string('a', 59)}"), uint.MaxValue);
    }

    private static byte[] Encode(InventoryChanged message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    [TestCase(10, (byte)0x03)]
    [TestCase(11, (byte)0x00)]
    [TestCase(12, (byte)0x0D)]
    [TestCase(13, (byte)0x00)]
    [TestCase(23, (byte)0x6A)]
    [TestCase(29, (byte)0x00)]
    [TestCase(33, (byte)0x03)]
    public void InventorySnapshot_WithABadPartACountAnIdAnItemAZeroQuantityOrASlot_IsRefused(int offset, byte value)
    {
        byte[] bytes = WireMatrix.With(SnapshotBytes, offset, value);

        Assert.That(InventorySnapshot.TryRead(bytes, out _), Is.False);
    }

    [TestCase(14, (byte)0x0D)]
    [TestCase(15, (byte)0x00)]
    [TestCase(25, (byte)0x6A)]
    [TestCase(35, (byte)0x03)]
    [TestCase(35, (byte)0x01)]
    public void InventoryChanged_WithABadCountAnIdAnItemOrASlot_IsRefused(int offset, byte value)
    {
        byte[] bytes = WireMatrix.With(ChangedBytes, offset, value);

        Assert.That(InventoryChanged.TryRead(bytes, out _), Is.False);
    }

    [TestCase(EquipmentSlot.Weapon)]
    [TestCase(EquipmentSlot.Armor)]
    public void InventoryMessages_WithTwoRowsWornInOneSlot_AreRefused(EquipmentSlot slot)
    {
        InventoryEntry[] entries = { new(11, ItemA, 1, slot), new(12, ItemA, 1, slot) };

        bool isSnapshotRead = InventorySnapshot.TryRead(Encode(new InventorySnapshot(7, 0, 0, 1, entries)), out _);
        bool isChangeRead = InventoryChanged.TryRead(Encode(new InventoryChanged(7, 8, 0, entries)), out _);

        Assert.That((isSnapshotRead, isChangeRead), Is.EqualTo((false, false)));
    }

    [Test]
    public void CreateParts_OfAHundredRows_ReassemblesToTheSameInventory_EveryPartWithTheCoins()
    {
        InventoryEntry[] entries =
            Enumerable.Range(1, 100).Select(id => new InventoryEntry(id, ItemA, (uint)id)).ToArray();

        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(42, 1_000_000_000, entries);
        var reassembled = new List<InventoryEntry>();
        foreach (InventorySnapshot part in parts)
        {
            Assert.That(InventorySnapshot.TryRead(Encode(part), out InventorySnapshot? read), Is.True);
            Assert.That((read!.Revision, read.Coins), Is.EqualTo((42u, 1_000_000_000u)));
            reassembled.AddRange(read.Entries);
        }

        Assert.That(parts, Has.Count.EqualTo(9));
        Assert.That(parts.Select(part => (int)part.Part), Is.EqualTo(Enumerable.Range(0, 9)));
        Assert.That(parts.All(part => part.PartCount == 9), Is.True);
        Assert.That(parts[8].IsLast, Is.True);
        Assert.That(parts[8].Entries, Has.Count.EqualTo(4));
        Assert.That(reassembled.Select(entry => entry.InventoryItem),
            Is.EqualTo(entries.Select(entry => entry.InventoryItem)));
        Assert.That(reassembled.Select(entry => entry.Quantity), Is.EqualTo(entries.Select(entry => entry.Quantity)));
    }

    [Test]
    public void CreateParts_OfAnEmptyInventory_IsOneEmptyLastPart()
    {
        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(0, 0, Array.Empty<InventoryEntry>());

        Assert.That(parts, Has.Count.EqualTo(1));
        Assert.That(parts[0].IsLast, Is.True);
        Assert.That(parts[0].Entries, Is.Empty);
        Assert.That(Encode(parts[0]), Is.EqualTo(new byte[] { 0x0F, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x01, 0x00 }));
    }

    [Test]
    public void InventoryChanged_AtItsLargest_FitsOneDatagram()
    {
        InventoryEntry[] entries =
            Enumerable.Range(1, InventoryChanged.MaxChanges).Select(id => LargestEntry(id)).ToArray();
        var message = new InventoryChanged(uint.MaxValue - 1, uint.MaxValue, 1_000_000_000, entries);

        bool isRead = InventoryChanged.TryRead(Encode(message), out InventoryChanged? read);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(975));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Changes, Has.Count.EqualTo(12));
    }

    [Test]
    public void InventoryChanged_ForGoldenBytes_RoundTrips()
    {
        var message = new InventoryChanged(7, 8, 250, new[] { new InventoryEntry(11, ItemA, 0) });
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        bool isRead = InventoryChanged.TryRead(ChangedBytes, out InventoryChanged? read);

        Assert.That(written, Is.EqualTo(ChangedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.PriorRevision, Is.EqualTo(7u));
        Assert.That(read.NewRevision, Is.EqualTo(8u));
        Assert.That(read.Coins, Is.EqualTo(250u));
        Assert.That(read.Changes[0].Quantity, Is.EqualTo(0u));
    }

    [Test]
    public void InventoryChanged_OfASwap_CarriesBothRowsSlots()
    {
        var message = new InventoryChanged(
            7,
            8,
            0,
            new[] { new InventoryEntry(12, ItemA, 1, EquipmentSlot.Weapon), new InventoryEntry(11, ItemA, 1) });

        bool isRead = InventoryChanged.TryRead(Encode(message), out InventoryChanged? read);

        Assert.That(isRead, Is.True);
        Assert.That(
            read!.Changes.Select(entry => (entry.InventoryItem, entry.Slot)),
            Is.EqualTo(new[] { (12L, EquipmentSlot.Weapon), (11L, EquipmentSlot.None) }));
    }

    [Test]
    public void InventoryChanged_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(ChangedBytes, ReadChanged);
        WireMatrix.AssertRejectsTrailingData(ChangedBytes, ReadChanged);
        WireMatrix.AssertRejectsOtherOpcodes(ChangedBytes, ReadChanged);
    }

    [Test]
    public void InventoryChanged_WhereOnlyTheCoinsMoved_CarriesNoRow()
    {
        var message = new InventoryChanged(7, 8, 250, Array.Empty<InventoryEntry>());

        bool isRead = InventoryChanged.TryRead(CoinsOnlyBytes, out InventoryChanged? read);

        Assert.That(Encode(message), Is.EqualTo(CoinsOnlyBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read!.NewRevision, read.Coins, read.Changes.Count), Is.EqualTo((8u, 250u, 0)));
        WireMatrix.AssertRejectsEveryTruncation(CoinsOnlyBytes, ReadChanged);
        WireMatrix.AssertRejectsTrailingData(CoinsOnlyBytes, ReadChanged);
    }

    [Test]
    public void InventoryMessages_WithCoinsAboveTheCap_AreRefused_AndCannotBeBuilt()
    {
        byte[] snapshotAbove = WireMatrix.With(SnapshotBytes, SnapshotCoins, AboveTheCap);
        byte[] changeAbove = WireMatrix.With(ChangedBytes, ChangedCoins, AboveTheCap);
        byte[] coinsOnlyAbove = WireMatrix.With(CoinsOnlyBytes, ChangedCoins, AboveTheCap);
        Action snapshot = () => _ = new InventorySnapshot(7, 1_000_000_001, 0, 1, Array.Empty<InventoryEntry>());
        Action change = () => _ = new InventoryChanged(7, 8, 1_000_000_001, Array.Empty<InventoryEntry>());

        Assert.That(InventorySnapshot.TryRead(snapshotAbove, out _), Is.False);
        Assert.That(InventoryChanged.TryRead(changeAbove, out _), Is.False);
        Assert.That(InventoryChanged.TryRead(coinsOnlyAbove, out _), Is.False);
        Assert.That(snapshot, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(change, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void InventoryResyncRequest_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[InventoryResyncRequest.EncodedLength];

        int length = new InventoryResyncRequest().Write(written);

        Assert.That(length, Is.EqualTo(2));
        Assert.That(written, Is.EqualTo(ResyncBytes));
        Assert.That(InventoryResyncRequest.TryRead(ResyncBytes, out _), Is.True);
    }

    [Test]
    public void InventoryResyncRequest_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(ResyncBytes, ReadResync);
        WireMatrix.AssertRejectsTrailingData(ResyncBytes, ReadResync);
        WireMatrix.AssertRejectsOtherOpcodes(ResyncBytes, ReadResync);
    }

    [Test]
    public void InventorySnapshot_AtItsLargest_FitsOneDatagram()
    {
        InventoryEntry[] entries =
            Enumerable.Range(1, InventorySnapshot.MaxEntries).Select(id => LargestEntry(id)).ToArray();
        var message = new InventorySnapshot(uint.MaxValue, 1_000_000_000, 0, 1, entries);

        bool isRead = InventorySnapshot.TryRead(Encode(message), out InventorySnapshot? read);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(973));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Entries, Has.Count.EqualTo(12));
    }

    [Test]
    public void InventorySnapshot_ForGoldenBytes_RoundTrips()
    {
        var message = new InventorySnapshot(
            7,
            250,
            1,
            3,
            new[] { new InventoryEntry(11, ItemA, 2, EquipmentSlot.Weapon) });

        bool isRead = InventorySnapshot.TryRead(SnapshotBytes, out InventorySnapshot? read);

        Assert.That(Encode(message), Is.EqualTo(SnapshotBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Revision, Is.EqualTo(7u));
        Assert.That(read.Coins, Is.EqualTo(250u));
        Assert.That(read.Part, Is.EqualTo(1));
        Assert.That(read.PartCount, Is.EqualTo(3));
        Assert.That(read.IsLast, Is.False);
        Assert.That(read.Entries[0].InventoryItem, Is.EqualTo(11L));
        Assert.That(read.Entries[0].Item, Is.EqualTo(ItemA));
        Assert.That(read.Entries[0].Quantity, Is.EqualTo(2u));
        Assert.That(read.Entries[0].Slot, Is.EqualTo(EquipmentSlot.Weapon));
    }

    [Test]
    public void InventorySnapshot_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(SnapshotBytes, ReadSnapshot);
        WireMatrix.AssertRejectsTrailingData(SnapshotBytes, ReadSnapshot);
        WireMatrix.AssertRejectsOtherOpcodes(SnapshotBytes, ReadSnapshot);
    }

    [Test]
    public void InventorySnapshot_WithThirteenEntries_CannotBeBuilt()
    {
        InventoryEntry[] entries = Enumerable.Range(1, 13).Select(id => new InventoryEntry(id, ItemA, 1)).ToArray();

        Action build = () => _ = new InventorySnapshot(1, 0, 0, 1, entries);

        Assert.That(build, Throws.ArgumentException);
    }

    // A refine level travels from protocol 37 and belongs to a row of one: never to a stack, nor to a removed row
    // (Network Protocol §6).
    [Test]
    public void RefineLevel_TravelsOnARowOfOne_AndIsMalformedOnAStackOrARemovedRow()
    {
        var item = new ItemDefinitionId("item.a");
        var refined =
            new InventorySnapshot(1, 0, 0, 1, new[] { new InventoryEntry(5, item, 1, EquipmentSlot.Weapon, 7) });
        var stack = new InventorySnapshot(1, 0, 0, 1, new[] { new InventoryEntry(5, item, 2, EquipmentSlot.None, 7) });
        var removed = new InventoryChanged(1, 2, 0, new[] { new InventoryEntry(5, item, 0, EquipmentSlot.None, 7) });

        Assert.That(InventorySnapshot.TryRead(Encode(refined), out InventorySnapshot? read), Is.True);
        Assert.That(read!.Entries[0].RefineLevel, Is.EqualTo(7));
        Assert.That(InventorySnapshot.TryRead(Encode(stack), out _), Is.False, "a stack");
        Assert.That(InventoryChanged.TryRead(Encode(removed), out _), Is.False, "a removed row");
    }
}
}
