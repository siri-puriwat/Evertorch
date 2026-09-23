using System;
using System.Threading;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     The pickup transaction: inventory and ledger change together or not at all, and a drop is committed at most
///     once (Persistence §5).
/// </summary>
[TestFixture]
public sealed class PickupStoreTests
{
    private const string Gel = "item.material.slime_gel";
    private const int StackLimit = 999;
    private const int MaxRows = 100;

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;
    private Sql m_sql = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
        m_sql = new Sql(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private long NewCharacter()
    {
        return m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Pick"));
    }

    private PickupResult Commit(long character, Guid drop, int amount, int stackLimit = StackLimit,
        int maxRows = MaxRows)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(drop, character, Gel, amount, stackLimit, maxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private PickupResult? Find(long character, Guid drop)
    {
        return m_store.FindPickupAsync(drop, character, CancellationToken.None).GetAwaiter().GetResult();
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private long Held(long character)
    {
        return m_sql.Scalar(
            $"SELECT coalesce(sum(quantity), 0) FROM inventory_items WHERE character_id = {character}");
    }

    private long LedgerEntries(Guid drop)
    {
        return m_sql.Scalar($"SELECT count(*) FROM economy_ledger WHERE operation_id = '{drop}'");
    }

    [TestCase(8, 2, 10, PickupStatus.Committed)]
    [TestCase(8, 3, 10, PickupStatus.InventoryFull)]
    [TestCase(0, 11, 10, PickupStatus.InventoryFull)]
    public void Commit_AgainstTheStackLimit_IsAllOrNothing(int held, int amount, int limit, PickupStatus expected)
    {
        long character = NewCharacter();
        if (held > 0)
        {
            Commit(character, Guid.NewGuid(), held, limit);
        }

        long revisionBefore = Revision(character);
        var drop = Guid.NewGuid();

        PickupResult result = Commit(character, drop, amount, limit);

        Assert.That(result.Status, Is.EqualTo(expected));
        bool isCommitted = expected == PickupStatus.Committed;
        Assert.That(Held(character), Is.EqualTo(isCommitted ? held + amount : held));
        Assert.That(Revision(character), Is.EqualTo(isCommitted ? revisionBefore + 1 : revisionBefore));
        Assert.That(LedgerEntries(drop), Is.EqualTo(isCommitted ? 1 : 0));
    }

    [Test]
    public void Commit_AtTheLastRevision_WrapsToZero()
    {
        long character = NewCharacter();
        m_sql.Execute($"UPDATE characters SET inventory_revision = 4294967295 WHERE id = {character}");

        PickupResult result = Commit(character, Guid.NewGuid(), 1);

        Assert.That(result.InventoryRevision, Is.EqualTo(0u));
        Assert.That(Revision(character), Is.EqualTo(0));
    }

    [Test]
    public void Commit_IntoAnEmptyInventory_AddsARowRaisesTheRevisionAndRecordsTheDrop()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();

        PickupResult result = Commit(character, drop, 3);

        Assert.That(result.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(result.InventoryRevision, Is.EqualTo(1u));
        Assert.That(result.Row!.ItemDefinitionId, Is.EqualTo(Gel));
        Assert.That(result.Row.Quantity, Is.EqualTo(3));
        Assert.That(Revision(character), Is.EqualTo(1));
        Assert.That(Held(character), Is.EqualTo(3));
        Assert.That(
            m_sql.Scalar(
                "SELECT count(*) FROM economy_ledger WHERE "
                + $"operation_id = '{drop}' AND actor_character_id = {character} AND operation_type = 'pickup' "
                + $"AND item_instance_id = {result.Row.Id} AND item_definition_id = '{Gel}' AND quantity_delta = 3 "
                + "AND currency_delta = 0"),
            Is.EqualTo(1));
    }

    [Test]
    public void Commit_NeedingOneRowTooMany_IsRefusedWhileMergingStillWorks()
    {
        long character = NewCharacter();
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, 'item.material.other', 1, 0, 0)");
        var refused = Guid.NewGuid();

        PickupResult full = Commit(character, refused, 1, maxRows: 1);
        PickupResult added = Commit(character, Guid.NewGuid(), 1, maxRows: 2);
        PickupResult merged = Commit(character, Guid.NewGuid(), 1, maxRows: 2);

        Assert.That(full.Status, Is.EqualTo(PickupStatus.InventoryFull));
        Assert.That(LedgerEntries(refused), Is.EqualTo(0));
        Assert.That(added.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(merged.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(merged.Row!.Quantity, Is.EqualTo(2));
    }

    [Test]
    public void Commit_OfADropAnotherCharacterHolds_ChangesNothing()
    {
        long first = NewCharacter();
        long second = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(first, drop, 1);

        PickupResult result = Commit(second, drop, 1);

        Assert.That(result.Status, Is.EqualTo(PickupStatus.TakenByOther));
        Assert.That(Held(second), Is.EqualTo(0));
        Assert.That(Revision(second), Is.EqualTo(0));
        Assert.That(LedgerEntries(drop), Is.EqualTo(1));
    }

    [Test]
    public void Commit_OfAnItemAlreadyHeld_MergesIntoItsRow()
    {
        long character = NewCharacter();
        long row = Commit(character, Guid.NewGuid(), 2).Row!.Id;

        PickupResult result = Commit(character, Guid.NewGuid(), 5);

        Assert.That(result.Row!.Id, Is.EqualTo(row));
        Assert.That(result.Row.Quantity, Is.EqualTo(7));
        Assert.That(result.InventoryRevision, Is.EqualTo(2u));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE character_id = {character}"),
            Is.EqualTo(1));
    }

    [Test]
    public void Commit_OfTheSameDropAgain_ChangesNothingAndReportsItCommitted()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(character, drop, 4);

        PickupResult again = Commit(character, drop, 4);

        Assert.That(again.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(again.InventoryRevision, Is.EqualTo(1u));
        Assert.That(again.Row!.Quantity, Is.EqualTo(4));
        Assert.That(Held(character), Is.EqualTo(4));
        Assert.That(LedgerEntries(drop), Is.EqualTo(1));
    }

    [Test]
    public void Find_ForADropNeverCommitted_IsNull()
    {
        Assert.That(Find(NewCharacter(), Guid.NewGuid()), Is.Null);
    }

    [Test]
    public void Find_ForAnotherCharacter_IsTakenByOther()
    {
        long holder = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(holder, drop, 2);

        Assert.That(Find(NewCharacter(), drop)!.Status, Is.EqualTo(PickupStatus.TakenByOther));
    }

    [Test]
    public void Find_ForTheCharacterThatHoldsTheDrop_ReportsTheInventoryNow()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(character, drop, 2);
        Commit(character, Guid.NewGuid(), 3);

        PickupResult? found = Find(character, drop);

        Assert.That(found!.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(found.InventoryRevision, Is.EqualTo(2u));
        Assert.That(found.Row!.Quantity, Is.EqualTo(5));
    }
}
}
