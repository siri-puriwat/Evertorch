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

        public uint SendEquip(long inventoryItem)
        {
            Sent.Add($"equip {inventoryItem}");
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
        Assert.That(commands.Sent, Is.EqualTo(new[] { "equip 11", "unequip Weapon", "unequip Armor" }));
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
