using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     What a press of an inventory row asks the server for (Prototype Content §2).
/// </summary>
[TestFixture]
public sealed class InventoryActionsTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.training_sword");
    private static readonly ItemDefinitionId Cloth = new("item.armor.cloth");
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");

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
    }

    [TestCase(ItemType.Material)]
    [TestCase(ItemType.Consumable)]
    public void Press_OfAnyOtherRow_AsksForNothing(ItemType type)
    {
        var commands = new Commands();

        uint sequence = InventoryActions.Press(commands, new InventoryEntry(13, Gel, 3), type);

        Assert.That(sequence, Is.Zero);
        Assert.That(commands.Sent, Is.Empty);
        Assert.That(InventoryActions.HasAction(type), Is.False);
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
}
}
