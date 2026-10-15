using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class EquipmentPositionsTests
{
    private static readonly ItemType[] NotWorn = { ItemType.Material, ItemType.Consumable, ItemType.Card };

    [TestCase(ItemType.Weapon, EquipmentSlot.Weapon)]
    [TestCase(ItemType.Armor, EquipmentSlot.Armor)]
    [TestCase(ItemType.Headgear, EquipmentSlot.Headgear)]
    [TestCase(ItemType.Shield, EquipmentSlot.Shield)]
    [TestCase(ItemType.Garment, EquipmentSlot.Garment)]
    [TestCase(ItemType.Footwear, EquipmentSlot.Footwear)]
    public void Of_ACategoryOfItsOwnPosition_IsThatPositionAlone(ItemType type, EquipmentSlot slot)
    {
        Assert.That(EquipmentPositions.Of(type), Is.EqualTo(new[] { slot }));
        Assert.That(EquipmentPositions.CategoryOf(slot), Is.EqualTo(type));
        Assert.That(
            EquipmentPositions.All.Where(position => EquipmentPositions.Fits(type, position)),
            Is.EqualTo(new[] { slot }),
            "it goes nowhere else");
    }

    [Test]
    public void All_IsTheEightPositionsInTheirWireOrder()
    {
        Assert.That(
            EquipmentPositions.All.Select(slot => (byte)slot),
            Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.That(
            Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>(),
            Is.EqualTo(new[] { EquipmentSlot.None }.Concat(EquipmentPositions.All)),
            "None and the eight, nothing else");
        Assert.That(EquipmentPositions.All.All(EquipmentPositions.IsPosition), Is.True);
        Assert.That(EquipmentPositions.IsPosition(EquipmentSlot.None), Is.False);
        Assert.That(EquipmentPositions.IsPosition((EquipmentSlot)9), Is.False);
    }

    [Test]
    public void CategoryOf_None_Throws()
    {
        Action read = () => EquipmentPositions.CategoryOf(EquipmentSlot.None);

        Assert.That(read, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ItemType_TheNewCategories_AreAppendedAfterTheCard()
    {
        Assert.That(
            Enum.GetValues(typeof(ItemType)).Cast<ItemType>().Select(type => (int)type),
            Is.EqualTo(Enumerable.Range(0, 10)));
        Assert.That(
            new[] { ItemType.Headgear, ItemType.Shield, ItemType.Garment, ItemType.Footwear, ItemType.Accessory }
                .Select(type => (int)type),
            Is.EqualTo(new[] { 5, 6, 7, 8, 9 }));
    }

    [Test]
    public void Of_AnAccessory_IsEitherAccessoryPosition()
    {
        Assert.That(
            EquipmentPositions.Of(ItemType.Accessory),
            Is.EqualTo(new[] { EquipmentSlot.Accessory1, EquipmentSlot.Accessory2 }));
        Assert.That(EquipmentPositions.CategoryOf(EquipmentSlot.Accessory1), Is.EqualTo(ItemType.Accessory));
        Assert.That(EquipmentPositions.CategoryOf(EquipmentSlot.Accessory2), Is.EqualTo(ItemType.Accessory));
    }

    [Test]
    public void Of_AnItemThatIsNotWorn_IsNowhere()
    {
        foreach (ItemType type in NotWorn)
        {
            Assert.That(EquipmentPositions.Of(type), Is.Empty, $"{type}");
            Assert.That(EquipmentPositions.IsEquipment(type), Is.False, $"{type}");
            Assert.That(EquipmentPositions.All.Any(slot => EquipmentPositions.Fits(type, slot)), Is.False, $"{type}");
        }
    }
}
}
