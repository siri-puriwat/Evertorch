using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     What a press of an inventory row or a potion slot asks the server for (Prototype Content §2, §4).
/// </summary>
[TestFixture]
public sealed class InventoryActionsTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.training_sword");
    private static readonly ItemDefinitionId Cloth = new("item.armor.cloth");
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");
    private static readonly ItemDefinitionId Potion = new("item.consumable.minor_health");

    private sealed class Commands : IItemCommandSink
    {
        public List<string> Sent { get; } = new();

        public uint SendEquip(long inventoryItem, EquipmentSlot destination)
        {
            Sent.Add($"equip {inventoryItem} {destination}");
            return (uint)Sent.Count;
        }

        public uint SendUnequip(EquipmentSlot slot)
        {
            Sent.Add($"unequip {slot}");
            return (uint)Sent.Count;
        }

        public uint SendUseItem(long inventoryItem)
        {
            Sent.Add($"use {inventoryItem}");
            return (uint)Sent.Count;
        }
    }

    // A card's press opens the Card window, which the interface does (Prototype Content §2): the sink hears nothing.
    [Test]
    public void Press_OfACard_AsksTheServerForNothing_ThoughItsRowIsAButton()
    {
        var commands = new Commands();

        uint sequence = InventoryActions.Press(
            commands,
            new InventoryEntry(15, new ItemDefinitionId("item.card.forest_crawler"), 2),
            ItemType.Card);

        Assert.That(sequence, Is.Zero);
        Assert.That(commands.Sent, Is.Empty);
        Assert.That(InventoryActions.HasAction(ItemType.Card), Is.True);
    }

    [Test]
    public void Press_OfAConsumable_UsesOneUnit()
    {
        var commands = new Commands();

        uint sequence = InventoryActions.Press(commands, new InventoryEntry(14, Potion, 3), ItemType.Consumable);

        Assert.That(sequence, Is.EqualTo(1u));
        Assert.That(commands.Sent, Is.EqualTo(new[] { "use 14" }));
        Assert.That(InventoryActions.HasAction(ItemType.Consumable), Is.True);
    }

    [Test]
    public void Press_OfAMaterial_AsksForNothing()
    {
        var commands = new Commands();

        uint sequence = InventoryActions.Press(commands, new InventoryEntry(13, Gel, 3), ItemType.Material);

        Assert.That(sequence, Is.Zero);
        Assert.That(commands.Sent, Is.Empty);
        Assert.That(InventoryActions.HasAction(ItemType.Material), Is.False);
    }

    [Test]
    public void Press_OfAWeaponOrArmor_PutsItOn_OrTakesItOffWhenWorn()
    {
        var commands = new Commands();

        uint equip = InventoryActions.Press(commands, new InventoryEntry(11, Sword, 1), ItemType.Weapon);
        uint unequip = InventoryActions.Press(
            commands,
            new InventoryEntry(11, Sword, 1, EquipmentSlot.Weapon),
            ItemType.Weapon);
        InventoryActions.Press(commands, new InventoryEntry(12, Cloth, 1, EquipmentSlot.Armor), ItemType.Armor);

        Assert.That((equip, unequip), Is.EqualTo((1u, 2u)));
        Assert.That(commands.Sent, Is.EqualTo(new[] { "equip 11 Weapon", "unequip Weapon", "unequip Armor" }));
    }

    // Each piece goes to its own position, and a worn piece comes off wherever it is worn; an unworn accessory's press
    // opens the Equipment window to choose one of the two, which the interface does (Prototype Content §2).
    [Test]
    public void Press_OfEachCategory_SendsItsOwnPosition_OrTakesItOffWhereItIsWorn()
    {
        var commands = new Commands();
        var item = new ItemDefinitionId("item.headgear.trail_cap");
        ItemType[] categories =
        {
            ItemType.Weapon, ItemType.Armor, ItemType.Headgear, ItemType.Shield, ItemType.Garment,
            ItemType.Footwear, ItemType.Accessory
        };

        foreach (ItemType category in categories)
        {
            InventoryActions.Press(commands, new InventoryEntry(20, item, 1), category);
        }

        InventoryActions.Press(commands, new InventoryEntry(21, item, 1, EquipmentSlot.Accessory2), ItemType.Accessory);

        Assert.That(
            commands.Sent,
            Is.EqualTo(
                new[]
                {
                    "equip 20 Weapon", "equip 20 Armor", "equip 20 Headgear", "equip 20 Shield", "equip 20 Garment",
                    "equip 20 Footwear", "unequip Accessory2"
                }));
        Assert.That(categories, Has.All.Matches<ItemType>(InventoryActions.HasAction));
        var unworn = new InventoryEntry(20, item, 1);
        var worn = new InventoryEntry(21, item, 1, EquipmentSlot.Accessory2);
        Assert.That(
            InventoryActions.ChoosesPosition(unworn, ItemType.Accessory),
            Is.True,
            "an unworn accessory chooses its position");
        Assert.That(InventoryActions.ChoosesPosition(worn, ItemType.Accessory), Is.False, "a worn one comes off");
        Assert.That(InventoryActions.ChoosesPosition(unworn, ItemType.Headgear), Is.False);
    }

    [Test]
    public void TryFindRow_ForAPotionSlot_TakesTheLowestIdRowOfItsItem_AndCountOfAddsThemUp()
    {
        InventoryEntry[] rows =
        {
            new(30, Potion, 2), new(12, Gel, 5), new(21, Potion, 50), new(40, Sword, 1)
        };

        bool isFound = InventoryActions.TryFindRow(rows, Potion, out InventoryEntry row);
        bool isCloth = InventoryActions.TryFindRow(rows, Cloth, out InventoryEntry _);

        Assert.That((isFound, row.InventoryItem), Is.EqualTo((true, 21L)));
        Assert.That(isCloth, Is.False);
        Assert.That(InventoryActions.CountOf(rows, Potion), Is.EqualTo(52));
    }
}
}
