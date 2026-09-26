using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the inventory, equipment, and item-use messages, so both compilers and runtimes
///     agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedInventoryMessageTests
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

    private static readonly byte[] EquipBytes =
    {
        0x10, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly byte[] UnequipBytes =
    {
        0x11, 0x00,
        0x02,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly byte[] UseBytes =
    {
        0x12, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");

    [Test]
    public void EquipItem_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[EquipItem.EncodedLength];
        new EquipItem(0x0102030405060708L, 0x0A0B0C0D).Write(buffer);

        bool isRead = EquipItem.TryRead(EquipBytes, out EquipItem read);

        Assert.That(buffer, Is.EqualTo(EquipBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.InventoryItem, Is.EqualTo(0x0102030405060708L));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }

    [Test]
    public void InventoryChanged_WriteAndRead_MatchGoldenBytes()
    {
        var message = new InventoryChanged(7, 8, new[] { new InventoryEntry(11, ItemA, 0) });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = InventoryChanged.TryRead(ChangedBytes, out InventoryChanged? read);

        Assert.That(buffer, Is.EqualTo(ChangedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.NewRevision, Is.EqualTo(8u));
        Assert.That(read.Changes[0].Quantity, Is.EqualTo(0u));
    }

    [Test]
    public void InventoryResyncRequest_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[InventoryResyncRequest.EncodedLength];
        new InventoryResyncRequest().Write(buffer);

        Assert.That(buffer, Is.EqualTo(ResyncBytes));
        Assert.That(InventoryResyncRequest.TryRead(ResyncBytes, out _), Is.True);
    }

    [Test]
    public void InventorySnapshot_AtItsLargest_Is957Bytes()
    {
        var item = new ItemDefinitionId($"item.{new string('a', 59)}");
        InventoryEntry[] entries = Enumerable.Range(1, InventorySnapshot.MaxEntries)
            .Select(id => new InventoryEntry(id, item, uint.MaxValue))
            .ToArray();

        Assert.That(new InventorySnapshot(uint.MaxValue, 0, 1, entries).GetEncodedLength(), Is.EqualTo(957));
    }

    [Test]
    public void InventorySnapshot_WriteAndRead_MatchGoldenBytes()
    {
        var message = new InventorySnapshot(7, 1, 3, new[] { new InventoryEntry(11, ItemA, 2, EquipmentSlot.Weapon) });
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = InventorySnapshot.TryRead(SnapshotBytes, out InventorySnapshot? read);

        Assert.That(buffer, Is.EqualTo(SnapshotBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Entries[0].InventoryItem, Is.EqualTo(11L));
        Assert.That(read.Entries[0].Item, Is.EqualTo(ItemA));
        Assert.That(read.Entries[0].Slot, Is.EqualTo(EquipmentSlot.Weapon));
    }

    [Test]
    public void MessageRouting_ForInventoryMessages_UsesTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[]
                 {
                     MessageOpcode.InventoryResyncRequest, MessageOpcode.InventorySnapshot,
                     MessageOpcode.InventoryChanged, MessageOpcode.EquipItem, MessageOpcode.UnequipItem,
                     MessageOpcode.UseItem
                 })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }

    [Test]
    public void UnequipItem_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[UnequipItem.EncodedLength];
        new UnequipItem(EquipmentSlot.Armor, 0x0A0B0C0D).Write(buffer);

        bool isRead = UnequipItem.TryRead(UnequipBytes, out UnequipItem read);

        Assert.That(buffer, Is.EqualTo(UnequipBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Slot, Is.EqualTo(EquipmentSlot.Armor));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }

    [Test]
    public void UseItem_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[UseItem.EncodedLength];
        new UseItem(0x0102030405060708L, 0x0A0B0C0D).Write(buffer);

        bool isRead = UseItem.TryRead(UseBytes, out UseItem read);

        Assert.That(buffer, Is.EqualTo(UseBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.InventoryItem, Is.EqualTo(0x0102030405060708L));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }
}
}
