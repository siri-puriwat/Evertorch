using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     The equipment commands of Network Protocol §8, their golden bytes, and what a reader refuses.
/// </summary>
[TestFixture]
public sealed class EquipmentMessageTests
{
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

    private static bool ReadEquip(byte[] bytes)
    {
        return EquipItem.TryRead(bytes, out _);
    }

    private static bool ReadUnequip(byte[] bytes)
    {
        return UnequipItem.TryRead(bytes, out _);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void EquipItem_ForARowTheDatabaseNeverGives_IsRefused(long inventoryItem)
    {
        byte[] bytes = new byte[EquipItem.EncodedLength];
        new EquipItem(inventoryItem, 1).Write(bytes);

        Assert.That(EquipItem.TryRead(bytes, out _), Is.False);
    }

    [TestCase((byte)0)]
    [TestCase((byte)3)]
    [TestCase((byte)255)]
    public void UnequipItem_ForASlotOtherThanTheWeaponsOrTheArmors_IsRefused(byte slot)
    {
        Assert.That(UnequipItem.TryRead(WireMatrix.With(UnequipBytes, 2, slot), out _), Is.False);
    }

    [TestCase((byte)1)]
    [TestCase((byte)2)]
    public void UnequipItem_ForTheWeaponOrTheArmor_IsRead(byte slot)
    {
        Assert.That(UnequipItem.TryRead(WireMatrix.With(UnequipBytes, 2, slot), out UnequipItem read), Is.True);
        Assert.That((byte)read.Slot, Is.EqualTo(slot));
    }

    [Test]
    public void EquipItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[EquipItem.EncodedLength];
        new EquipItem(0x0102030405060708L, 0x0A0B0C0D).Write(written);

        bool isRead = EquipItem.TryRead(EquipBytes, out EquipItem read);

        Assert.That(written, Is.EqualTo(EquipBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.InventoryItem, read.CommandSequence), Is.EqualTo((0x0102030405060708L, 0x0A0B0C0Du)));
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
            new[] { (byte)EquipmentSlot.None, (byte)EquipmentSlot.Weapon, (byte)EquipmentSlot.Armor },
            Is.EqualTo(new byte[] { 0, 1, 2 }));
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
