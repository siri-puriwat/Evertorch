using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The server's copy of a character's inventory takes each committed row as it now is, with its slot
///     (Persistence §5).
/// </summary>
[TestFixture]
public sealed class CharacterInventoryTests
{
    private const string GelId = "item.material.slime_gel";
    private const string SwordId = "item.weapon.training_sword";
    private const string ArmorId = "item.armor.cloth";

    private static readonly ItemDefinitionId Gel = new(GelId);

    private static InventoryResult Result(uint revision, params StoredItem[] rows)
    {
        return new InventoryResult(InventoryStatus.Committed, revision, 0, rows);
    }

    private static CharacterInventory Loaded(params StoredItem[] items)
    {
        return Loaded(0, items);
    }

    private static CharacterInventory Loaded(long coins, params StoredItem[] items)
    {
        return CharacterInventory.FromStored(
            new StoredCharacter(
                1,
                new AccountId(1),
                "Tester",
                "job.adventurer",
                1,
                0,
                new PrimaryStats(5, 5, 5, 5, 5, 5),
                71,
                24,
                "map.training_ground",
                new WorldPosition(0f, 0f, 0f),
                1,
                coins,
                items));
    }

    [Test]
    public void Apply_ChangesARow_AddsANewOne_AndDropsOneTheChangeEmptied()
    {
        var inventory =
            new CharacterInventory(4, new[] { new InventoryEntry(11, Gel, 3), new InventoryEntry(12, Gel, 1) });

        inventory.Apply(Result(5, new StoredItem(11, GelId, 5)));
        inventory.Apply(Result(6, new StoredItem(13, GelId, 2)));
        InventoryEntry[] told = inventory.Apply(Result(7, new StoredItem(12, GelId, 0)));

        Assert.That(
            inventory.Rows.Select(row => (row.InventoryItem, row.Quantity)),
            Is.EqualTo(new[] { (11L, 5u), (13L, 2u) }));
        Assert.That(inventory.Revision, Is.EqualTo(7u));
        Assert.That(told.Select(row => (row.InventoryItem, row.Quantity)), Is.EqualTo(new[] { (12L, 0u) }));
    }

    [Test]
    public void Apply_FollowsEachRowsSlot_ThroughALoadASwapAndAnUnequip()
    {
        CharacterInventory inventory = Loaded(
            new StoredItem(21, SwordId, 1, "Weapon"),
            new StoredItem(22, SwordId, 1),
            new StoredItem(23, ArmorId, 1, "Armor"));

        Assert.That(
            (inventory.WornIn(EquipmentSlot.Weapon), inventory.WornIn(EquipmentSlot.Armor)),
            Is.EqualTo((21L, 23L)));

        inventory.Apply(Result(2, new StoredItem(22, SwordId, 1, "Weapon"), new StoredItem(21, SwordId, 1)));
        inventory.Apply(Result(3, new StoredItem(23, ArmorId, 1)));

        Assert.That(
            (inventory.WornIn(EquipmentSlot.Weapon), inventory.WornIn(EquipmentSlot.Armor)),
            Is.EqualTo((22L, 0L)));
        Assert.That(inventory.WornIn(EquipmentSlot.None), Is.Zero);
        Assert.That(inventory.Rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void Coins_AreTheStoredOnes_ThenEachCommittedChanges_AndEveryPartOfTheSnapshotCarriesThem()
    {
        CharacterInventory inventory = Loaded(
            250,
            Enumerable.Range(1, 13).Select(id => new StoredItem(id, GelId, 1)).ToArray());
        long loaded = inventory.Coins;

        inventory.Apply(new InventoryResult(InventoryStatus.Committed, 2, 1_000_000_000, Array.Empty<StoredItem>()));

        Assert.That(loaded, Is.EqualTo(250));
        Assert.That(inventory.Coins, Is.EqualTo(1_000_000_000));
        Assert.That(inventory.Revision, Is.EqualTo(2u));
        Assert.That(
            inventory.CreateSnapshot().Select(part => part.Coins),
            Is.EqualTo(new[] { 1_000_000_000u, 1_000_000_000u }));
    }

    [Test]
    public void SlotOf_AStoredSlotNoSlotHas_Throws()
    {
        Action read = () => CharacterInventory.SlotOf(new StoredItem(1, SwordId, 1, "Shield"));

        Assert.That(read, Throws.InstanceOf<InvalidOperationException>());
    }
}
}
