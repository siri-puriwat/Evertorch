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

    private static readonly byte[] SnapshotBytes =
    {
        0x0F, 0x80, 0x07, 0x00, 0x00, 0x00, 0x01, 0x03, 0x01,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x02, 0x00, 0x00, 0x00,
        0x01
    };

    private static readonly byte[] ChangedBytes =
    {
        0x10, 0x80, 0x07, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01,
        0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x00, 0x00, 0x00, 0x00,
        0x00
    };

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

    [TestCase(6, (byte)0x03)]
    [TestCase(7, (byte)0x00)]
    [TestCase(8, (byte)0x0D)]
    [TestCase(9, (byte)0x00)]
    [TestCase(19, (byte)0x6A)]
    [TestCase(25, (byte)0x00)]
    [TestCase(29, (byte)0x03)]
    public void InventorySnapshot_WithABadPartACountAnIdAnItemAZeroQuantityOrASlot_IsRefused(int offset, byte value)
    {
        byte[] bytes = WireMatrix.With(SnapshotBytes, offset, value);

        Assert.That(InventorySnapshot.TryRead(bytes, out _), Is.False);
    }

    [TestCase(10, (byte)0x00)]
    [TestCase(10, (byte)0x0D)]
    [TestCase(11, (byte)0x00)]
    [TestCase(21, (byte)0x6A)]
    [TestCase(31, (byte)0x03)]
    [TestCase(31, (byte)0x01)]
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

        bool isSnapshotRead = InventorySnapshot.TryRead(Encode(new InventorySnapshot(7, 0, 1, entries)), out _);
        bool isChangeRead = InventoryChanged.TryRead(Encode(new InventoryChanged(7, 8, entries)), out _);

        Assert.That((isSnapshotRead, isChangeRead), Is.EqualTo((false, false)));
    }

    [Test]
    public void CreateParts_OfAHundredRows_ReassemblesToTheSameInventory()
    {
        InventoryEntry[] entries =
            Enumerable.Range(1, 100).Select(id => new InventoryEntry(id, ItemA, (uint)id)).ToArray();

        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(42, entries);
        var reassembled = new List<InventoryEntry>();
        foreach (InventorySnapshot part in parts)
        {
            Assert.That(InventorySnapshot.TryRead(Encode(part), out InventorySnapshot? read), Is.True);
            Assert.That(read!.Revision, Is.EqualTo(42u));
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
        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(0, Array.Empty<InventoryEntry>());

        Assert.That(parts, Has.Count.EqualTo(1));
        Assert.That(parts[0].IsLast, Is.True);
        Assert.That(parts[0].Entries, Is.Empty);
        Assert.That(Encode(parts[0]), Is.EqualTo(new byte[] { 0x0F, 0x80, 0, 0, 0, 0, 0x00, 0x01, 0x00 }));
    }

    [Test]
    public void InventoryChanged_AtItsLargest_FitsOneDatagram()
    {
        InventoryEntry[] entries =
            Enumerable.Range(1, InventoryChanged.MaxChanges).Select(id => LargestEntry(id)).ToArray();
        var message = new InventoryChanged(uint.MaxValue - 1, uint.MaxValue, entries);

        bool isRead = InventoryChanged.TryRead(Encode(message), out InventoryChanged? read);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(959));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Changes, Has.Count.EqualTo(12));
    }

    [Test]
    public void InventoryChanged_ForGoldenBytes_RoundTrips()
    {
        var message = new InventoryChanged(7, 8, new[] { new InventoryEntry(11, ItemA, 0) });
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        bool isRead = InventoryChanged.TryRead(ChangedBytes, out InventoryChanged? read);

        Assert.That(written, Is.EqualTo(ChangedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.PriorRevision, Is.EqualTo(7u));
        Assert.That(read.NewRevision, Is.EqualTo(8u));
        Assert.That(read.Changes[0].Quantity, Is.EqualTo(0u));
    }

    [Test]
    public void InventoryChanged_OfASwap_CarriesBothRowsSlots()
    {
        var message = new InventoryChanged(
            7,
            8,
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
    public void InventoryChanged_WithNoChanges_CannotBeBuilt()
    {
        Action build = () => _ = new InventoryChanged(1, 2, Array.Empty<InventoryEntry>());

        Assert.That(build, Throws.ArgumentException);
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
        var message = new InventorySnapshot(uint.MaxValue, 0, 1, entries);

        bool isRead = InventorySnapshot.TryRead(Encode(message), out InventorySnapshot? read);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(957));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Entries, Has.Count.EqualTo(12));
    }

    [Test]
    public void InventorySnapshot_ForGoldenBytes_RoundTrips()
    {
        var message = new InventorySnapshot(7, 1, 3, new[] { new InventoryEntry(11, ItemA, 2, EquipmentSlot.Weapon) });

        bool isRead = InventorySnapshot.TryRead(SnapshotBytes, out InventorySnapshot? read);

        Assert.That(Encode(message), Is.EqualTo(SnapshotBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Revision, Is.EqualTo(7u));
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

        Action build = () => _ = new InventorySnapshot(1, 0, 1, entries);

        Assert.That(build, Throws.ArgumentException);
    }
}
}
