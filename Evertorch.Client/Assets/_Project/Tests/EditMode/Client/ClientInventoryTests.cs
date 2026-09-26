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
        foreach (InventorySnapshot part in InventorySnapshot.CreateParts(revision, 0, rows))
        {
            inventory.OnSnapshot(part);
        }

        return inventory;
    }

    [Test]
    public void ChangeApplied_IsNotRaisedForASnapshotOrAnIgnoredChange()
    {
        var inventory = new ClientInventory();
        int applied = 0;
        inventory.ChangeApplied += _ => applied++;

        inventory.OnChanged(new InventoryChanged(0, 1, 50, new[] { Row(1, 1) }));
        inventory.OnSnapshot(new InventorySnapshot(3, 100, 0, 1, new[] { Row(1, 12) }));
        inventory.OnChanged(new InventoryChanged(4, 5, 124, new[] { Row(1, 0) }));

        Assert.That(applied, Is.Zero);
        Assert.That(inventory.Rows.Single().Quantity, Is.EqualTo(12u));
    }

    // What the feedback lines tell a purchase and a sale by (Prototype Content §2): the coins on either side of each
    // applied change and the units each item gained or lost, nothing for a row only put on.
    [Test]
    public void ChangeApplied_TellsTheCoinsAndTheUnitsOfEachItemTheChangeMoved()
    {
        var sword = new ItemDefinitionId("item.weapon.training_sword");
        var inventory = new ClientInventory();
        inventory.OnSnapshot(new InventorySnapshot(3, 100, 0, 1, new[] { Row(1, 12), Row(2, 3) }));
        var deltas = new List<InventoryDelta>();
        inventory.ChangeApplied += deltas.Add;

        inventory.OnChanged(new InventoryChanged(3, 4, 124, new[] { Row(1, 0), Row(2, 1) }));
        inventory.OnChanged(new InventoryChanged(4, 5, 74, new[] { new InventoryEntry(3, sword, 1) }));
        inventory.OnChanged(
            new InventoryChanged(5, 6, 74, new[] { new InventoryEntry(3, sword, 1, EquipmentSlot.Weapon) }));

        Assert.That(
            deltas.Select(delta => (delta.CoinsBefore, delta.CoinsAfter)),
            Is.EqualTo(new[] { (100u, 124u), (124u, 74u), (74u, 74u) }));
        Assert.That(
            deltas[0].Units.Select(pair => (pair.Key, pair.Value)),
            Is.EqualTo(new[] { (Gel, -14L) }),
            "two rows of one item");
        Assert.That(deltas[1].Units.Select(pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { (sword, 1L) }));
        Assert.That(deltas[2].Units, Is.Empty, "putting a row on moves no units");
    }

    [Test]
    public void Change_BeforeTheFirstSnapshot_IsIgnoredWithoutAResync()
    {
        var inventory = new ClientInventory();

        bool needsResync = inventory.OnChanged(new InventoryChanged(0, 1, 0, new[] { Row(1, 1) }));

        Assert.That(needsResync, Is.False);
        Assert.That(inventory.Rows, Is.Empty);
        Assert.That(inventory.IgnoredChanges, Is.EqualTo(1));
    }

    [Test]
    public void Change_FromAnotherRevision_AsksOnceForASnapshotAndIgnoresChangesUntilItArrives()
    {
        ClientInventory inventory = Current(3, Row(1, 5));

        bool first = inventory.OnChanged(new InventoryChanged(4, 5, 0, new[] { Row(1, 9) }));
        bool second = inventory.OnChanged(new InventoryChanged(5, 6, 0, new[] { Row(1, 10) }));

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That(inventory.Resyncs, Is.EqualTo(1));
        Assert.That(inventory.IgnoredChanges, Is.EqualTo(2));
        Assert.That(inventory.Rows.Single().Quantity, Is.EqualTo(5u));

        inventory.OnSnapshot(new InventorySnapshot(6, 0, 0, 1, new[] { Row(1, 10) }));
        bool afterSnapshot = inventory.OnChanged(new InventoryChanged(6, 7, 0, new[] { Row(1, 11) }));

        Assert.That(afterSnapshot, Is.False);
        Assert.That(inventory.IsCurrent, Is.True);
        Assert.That(inventory.Rows.Single().Quantity, Is.EqualTo(11u));
    }

    [Test]
    public void Change_FromTheCurrentRevision_AddsUpdatesAndRemovesRows()
    {
        ClientInventory inventory = Current(3, Row(1, 5), Row(2, 6));

        bool needsResync =
            inventory.OnChanged(new InventoryChanged(3, 4, 0, new[] { Row(1, 0), Row(2, 7), Row(3, 1) }));

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
                0,
                new[] { new InventoryEntry(2, sword, 1, EquipmentSlot.Weapon), new InventoryEntry(1, sword, 1) }));

        Assert.That(
            inventory.Rows.Select(row => (row.InventoryItem, row.Slot)),
            Is.EqualTo(new[] { (1L, EquipmentSlot.None), (2L, EquipmentSlot.Weapon) }));
    }

    [Test]
    public void Coins_ComeWithTheLastPart_ThenWithEachChangeInRevisionOrder()
    {
        IReadOnlyList<InventorySnapshot> parts =
            InventorySnapshot.CreateParts(3, 250, Enumerable.Range(1, 13).Select(id => Row(id, 1)).ToArray());
        var inventory = new ClientInventory();
        inventory.OnSnapshot(parts[0]);
        uint beforeTheLastPart = inventory.Coins;
        inventory.OnSnapshot(parts[1]);
        uint afterTheSnapshot = inventory.Coins;

        bool skipped = inventory.OnChanged(new InventoryChanged(5, 6, 900, new InventoryEntry[0]));
        inventory.OnSnapshot(new InventorySnapshot(4, 250, 0, 1, new InventoryEntry[0]));
        bool coinsOnly = inventory.OnChanged(new InventoryChanged(4, 5, 70, new InventoryEntry[0]));

        Assert.That((beforeTheLastPart, afterTheSnapshot), Is.EqualTo((0u, 250u)));
        Assert.That(skipped, Is.True, "a change from another revision moves no coins");
        Assert.That(coinsOnly, Is.False);
        Assert.That((inventory.Revision, inventory.Coins, inventory.Rows.Count), Is.EqualTo((5u, 70u, 0)));
    }

    [Test]
    public void Part_OfAnotherSnapshot_DiscardsTheUnfinishedOneAndAsksAgain()
    {
        ClientInventory inventory = Current(3, Row(1, 5));
        inventory.OnSnapshot(new InventorySnapshot(8, 0, 0, 2, new[] { Row(2, 1) }));

        bool needsResync = inventory.OnSnapshot(new InventorySnapshot(9, 0, 1, 2, new[] { Row(3, 1) }));

        Assert.That(needsResync, Is.True);
        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That(inventory.Rows.Single().InventoryItem, Is.EqualTo(1L));

        inventory.OnSnapshot(new InventorySnapshot(10, 0, 0, 1, new[] { Row(4, 1) }));

        Assert.That(inventory.IsCurrent, Is.True);
        Assert.That(inventory.Rows.Single().InventoryItem, Is.EqualTo(4L));
    }

    [Test]
    public void Parts_ThatDisagreeOnTheCoins_AreAGap()
    {
        ClientInventory inventory = Current(3, Row(1, 5));
        inventory.OnSnapshot(new InventorySnapshot(8, 250, 0, 2, new[] { Row(2, 1) }));

        bool needsResync = inventory.OnSnapshot(new InventorySnapshot(8, 260, 1, 2, new[] { Row(3, 1) }));

        Assert.That(needsResync, Is.True);
        Assert.That(inventory.IsCurrent, Is.False);
        Assert.That((inventory.Revision, inventory.Coins), Is.EqualTo((3u, 0u)), "nothing of the parts applied");
    }

    [Test]
    public void Snapshot_OfAHundredRowsInParts_IsAppliedWhenTheLastPartArrives()
    {
        InventoryEntry[] rows = Enumerable.Range(1, 100).Select(id => Row(id, (uint)id)).ToArray();
        IReadOnlyList<InventorySnapshot> parts = InventorySnapshot.CreateParts(42, 0, rows);
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

        inventory.OnSnapshot(new InventorySnapshot(9, 0, 0, 1, new[] { Row(4, 1) }));

        Assert.That(inventory.Revision, Is.EqualTo(9u));
        Assert.That(inventory.Rows.Select(row => row.InventoryItem), Is.EqualTo(new[] { 4L }));
    }
}
}
