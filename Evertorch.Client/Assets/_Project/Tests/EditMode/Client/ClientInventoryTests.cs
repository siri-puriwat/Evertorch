using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientInventoryTests
{
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");

    private static InventoryEntry Row(long id, uint quantity)
    {
        return new InventoryEntry(id, Gel, quantity);
    }

    private static ClientInventory Current(uint revision, params InventoryEntry[] rows)
    {
        var inventory = new ClientInventory();
        foreach (InventorySnapshot part in InventorySnapshot.CreateParts(revision, rows))
        {
            inventory.OnSnapshot(part);
        }

        return inventory;
    }

    [Test]
    public void Change_BeforeTheFirstSnapshot_IsIgnoredWithoutAResync()
    {
        var inventory = new ClientInventory();

        bool needsResync = inventory.OnChanged(new InventoryChanged(0, 1, new[] { Row(1, 1) }));

        Assert.That(needsResync, Is.False);
        Assert.That(inventory.Rows, Is.Empty);
        Assert.That(inventory.IgnoredChanges, Is.EqualTo(1));
    }

    [Test]
    public void Change_FromAnotherRevision_AsksOnceForASnapshotAndIgnoresChangesUntilItArrives()
    {
        ClientInventory inventory = Current(3, Row(1, 5));

        bool first = inventory.OnChanged(new InventoryChanged(4, 5, new[] { Row(1, 9) }));
        bool second = inventory.OnChanged(new InventoryChanged(5, 6, new[] { Row(1, 10) }));

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That(inventory.Resyncs, Is.EqualTo(1));
        Assert.That(inventory.IgnoredChanges, Is.EqualTo(2));
        Assert.That(inventory.Rows.Single().Quantity, Is.EqualTo(5u));

        inventory.OnSnapshot(new InventorySnapshot(6, 0, 1, new[] { Row(1, 10) }));
        bool afterSnapshot = inventory.OnChanged(new InventoryChanged(6, 7, new[] { Row(1, 11) }));

        Assert.That(afterSnapshot, Is.False);
        Assert.That(inventory.IsCurrent, Is.True);
        Assert.That(inventory.Rows.Single().Quantity, Is.EqualTo(11u));
    }

    [Test]
    public void Change_FromTheCurrentRevision_AddsUpdatesAndRemovesRows()
    {
        ClientInventory inventory = Current(3, Row(1, 5), Row(2, 6));

        bool needsResync = inventory.OnChanged(new InventoryChanged(3, 4, new[] { Row(1, 0), Row(2, 7), Row(3, 1) }));

        Assert.That(needsResync, Is.False);
        Assert.That(inventory.Revision, Is.EqualTo(4u));
        Assert.That(inventory.Rows.Select(row => row.InventoryItem), Is.EqualTo(new[] { 2L, 3L }));
        Assert.That(inventory.Rows.Select(row => row.Quantity), Is.EqualTo(new[] { 7u, 1u }));
    }

    [Test]
    public void Change_OfASwap_MovesTheSlotFromOneRowToTheOther()
    {
        var sword = new ItemDefinitionId("item.weapon.training_sword");
        ClientInventory inventory = Current(
            3,
            new InventoryEntry(1, sword, 1, EquipmentSlot.Weapon),
            new InventoryEntry(2, sword, 1));

        inventory.OnChanged(
            new InventoryChanged(
                3,
                4,
                new[] { new InventoryEntry(2, sword, 1, EquipmentSlot.Weapon), new InventoryEntry(1, sword, 1) }));

        Assert.That(
            inventory.Rows.Select(row => (row.InventoryItem, row.Slot)),
            Is.EqualTo(new[] { (1L, EquipmentSlot.None), (2L, EquipmentSlot.Weapon) }));
    }

    [Test]
    public void Part_OfAnotherSnapshot_DiscardsTheUnfinishedOneAndAsksAgain()
    {
        ClientInventory inventory = Current(3, Row(1, 5));
        inventory.OnSnapshot(new InventorySnapshot(8, 0, 2, new[] { Row(2, 1) }));

        bool needsResync = inventory.OnSnapshot(new InventorySnapshot(9, 1, 2, new[] { Row(3, 1) }));

        Assert.That(needsResync, Is.True);
        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That(inventory.Rows.Single().InventoryItem, Is.EqualTo(1L));

        inventory.OnSnapshot(new InventorySnapshot(10, 0, 1, new[] { Row(4, 1) }));

        Assert.That(inventory.IsCurrent, Is.True);
        Assert.That(inventory.Rows.Single().InventoryItem, Is.EqualTo(4L));
    }

    [Test]
    public void Snapshot_OfAHundredRowsInParts_IsAppliedWhenTheLastPartArrives()
    {
        InventoryEntry[] rows = Enumerable.Range(1, 100).Select(id => Row(id, (uint)id)).ToArray();
        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(42, rows);
        var inventory = new ClientInventory();
        int changed = 0;
        inventory.Changed += () => changed++;

        foreach (InventorySnapshot part in parts.Take(parts.Count - 1))
        {
            Assert.That(inventory.OnSnapshot(part), Is.False);
        }

        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That(inventory.Rows, Is.Empty);

        inventory.OnSnapshot(parts[parts.Count - 1]);

        Assert.That(parts.Count, Is.EqualTo(9));
        Assert.That(inventory.IsCurrent, Is.True);
        Assert.That(inventory.Revision, Is.EqualTo(42u));
        Assert.That(inventory.Rows.Select(row => row.InventoryItem), Is.EqualTo(rows.Select(row => row.InventoryItem)));
        Assert.That(inventory.Rows.Select(row => row.Quantity), Is.EqualTo(rows.Select(row => row.Quantity)));
        Assert.That(changed, Is.EqualTo(1));
    }

    [Test]
    public void Snapshot_ReplacesTheWholeInventory()
    {
        ClientInventory inventory = Current(3, Row(1, 5), Row(2, 6));

        inventory.OnSnapshot(new InventorySnapshot(9, 0, 1, new[] { Row(4, 1) }));

        Assert.That(inventory.Revision, Is.EqualTo(9u));
        Assert.That(inventory.Rows.Select(row => row.InventoryItem), Is.EqualTo(new[] { 4L }));
    }
}
}
