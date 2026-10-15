using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     The equipment commands of Network Protocol §6 and §8, their golden bytes, and what a reader refuses.
/// </summary>
[TestFixture]
public sealed class EquipmentMessageTests
{
    // Protocol 48: the row, its destination (accessory 2 here), and the sequence.
    private static readonly byte[] EquipBytes =
    {
        0x10, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x08,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly byte[] UnequipBytes =
    {
        0x11, 0x00,
        0x02,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static bool ReadEquip(byte[] bytes)
    {
        return EquipItem.TryRead(bytes, out _);
    }

    private static bool ReadUnequip(byte[] bytes)
    {
        return UnequipItem.TryRead(bytes, out _);
    }

    [TestCase((byte)0)]
    [TestCase((byte)9)]
    [TestCase((byte)255)]
    public void EquipItem_ForADestinationThatIsNoPosition_IsRefused(byte destination)
    {
        Assert.That(EquipItem.TryRead(WireMatrix.With(EquipBytes, 10, destination), out _), Is.False);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void EquipItem_ForARowTheDatabaseNeverGives_IsRefused(long inventoryItem)
    {
        byte[] bytes = new byte[EquipItem.EncodedLength];
        new EquipItem(inventoryItem, EquipmentSlot.Weapon, 1).Write(bytes);

        Assert.That(EquipItem.TryRead(bytes, out _), Is.False);
    }

    [TestCase((byte)0)]
    [TestCase((byte)9)]
    [TestCase((byte)255)]
    public void UnequipItem_ForASlotThatIsNoPosition_IsRefused(byte slot)
    {
        Assert.That(UnequipItem.TryRead(WireMatrix.With(UnequipBytes, 2, slot), out _), Is.False);
    }

    [Test]
    public void EquipItem_ForEachOfTheEightPositions_IsRead()
    {
        foreach (EquipmentSlot slot in EquipmentPositions.All)
        {
            Assert.That(
                EquipItem.TryRead(WireMatrix.With(EquipBytes, 10, (byte)slot), out EquipItem read),
                Is.True,
                $"{slot}");
            Assert.That(read.Destination, Is.EqualTo(slot));
        }
    }

    [Test]
    public void EquipItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[EquipItem.EncodedLength];
        new EquipItem(0x0102030405060708L, EquipmentSlot.Accessory2, 0x0A0B0C0D).Write(written);

        bool isRead = EquipItem.TryRead(EquipBytes, out EquipItem read);

        Assert.That(EquipItem.EncodedLength, Is.EqualTo(15));
        Assert.That(written, Is.EqualTo(EquipBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read.InventoryItem, read.Destination, read.CommandSequence),
            Is.EqualTo((0x0102030405060708L, EquipmentSlot.Accessory2, 0x0A0B0C0Du)));
    }

    [Test]
    public void EquipItem_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(EquipBytes, ReadEquip);
        WireMatrix.AssertRejectsTrailingData(EquipBytes, ReadEquip);
        WireMatrix.AssertRejectsOtherOpcodes(EquipBytes, ReadEquip);
    }

    [Test]
    public void EquipmentSlot_Values_KeepTheirWireNumbers()
    {
        Assert.That(
            new[]
            {
                (byte)EquipmentSlot.None, (byte)EquipmentSlot.Weapon, (byte)EquipmentSlot.Armor,
                (byte)EquipmentSlot.Headgear, (byte)EquipmentSlot.Shield, (byte)EquipmentSlot.Garment,
                (byte)EquipmentSlot.Footwear, (byte)EquipmentSlot.Accessory1, (byte)EquipmentSlot.Accessory2
            },
            Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Test]
    public void UnequipItem_ForEachOfTheEightPositions_IsRead()
    {
        foreach (EquipmentSlot slot in EquipmentPositions.All)
        {
            Assert.That(
                UnequipItem.TryRead(WireMatrix.With(UnequipBytes, 2, (byte)slot), out UnequipItem read),
                Is.True,
                $"{slot}");
            Assert.That(read.Slot, Is.EqualTo(slot));
        }
    }

    [Test]
    public void UnequipItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[UnequipItem.EncodedLength];
        new UnequipItem(EquipmentSlot.Armor, 0x0A0B0C0D).Write(written);

        bool isRead = UnequipItem.TryRead(UnequipBytes, out UnequipItem read);

        Assert.That(written, Is.EqualTo(UnequipBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.Slot, read.CommandSequence), Is.EqualTo((EquipmentSlot.Armor, 0x0A0B0C0Du)));
    }

    [Test]
    public void UnequipItem_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(UnequipBytes, ReadUnequip);
        WireMatrix.AssertRejectsTrailingData(UnequipBytes, ReadUnequip);
        WireMatrix.AssertRejectsOtherOpcodes(UnequipBytes, ReadUnequip);
    }
}
}
