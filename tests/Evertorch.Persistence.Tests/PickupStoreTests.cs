using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
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

    private InventoryResult Commit(long character, Guid drop, int amount, int stackLimit = StackLimit,
        int maxRows = MaxRows)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(drop, character, Gel, amount, stackLimit, maxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult? Find(long character, Guid drop)
    {
        return Find(character, drop, Array.Empty<long>());
    }

    private InventoryResult? Find(long character, Guid operation, long[] rowIds)
    {
        return m_store.FindOperationAsync(operation, character, rowIds, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private Task<InventoryResult?> FindInBackground(long character, Guid drop)
    {
        return Task.Run(() => Find(character, drop));
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

    [TestCase(8, 2, 10, InventoryStatus.Committed)]
    [TestCase(8, 3, 10, InventoryStatus.InventoryFull)]
    [TestCase(0, 11, 10, InventoryStatus.InventoryFull)]
    public void Commit_AgainstTheStackLimit_IsAllOrNothing(int held, int amount, int limit, InventoryStatus expected)
    {
        long character = NewCharacter();
        if (held > 0)
        {
            Commit(character, Guid.NewGuid(), held, limit);
        }

        long revisionBefore = Revision(character);
        var drop = Guid.NewGuid();

        InventoryResult result = Commit(character, drop, amount, limit);

        Assert.That(result.Status, Is.EqualTo(expected));
        bool isCommitted = expected == InventoryStatus.Committed;
        Assert.That(Held(character), Is.EqualTo(isCommitted ? held + amount : held));
        Assert.That(Revision(character), Is.EqualTo(isCommitted ? revisionBefore + 1 : revisionBefore));
        Assert.That(LedgerEntries(drop), Is.EqualTo(isCommitted ? 1 : 0));
    }

    [Test]
    public void Commit_AtTheLastRevision_WrapsToZero()
    {
        long character = NewCharacter();
        m_sql.Execute($"UPDATE characters SET inventory_revision = 4294967295 WHERE id = {character}");

        InventoryResult result = Commit(character, Guid.NewGuid(), 1);

        Assert.That(result.InventoryRevision, Is.EqualTo(0u));
        Assert.That(Revision(character), Is.EqualTo(0));
    }

    [Test]
    public void Commit_IntoAnEmptyInventory_AddsARowRaisesTheRevisionAndRecordsTheDrop()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();

        InventoryResult result = Commit(character, drop, 3);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(result.InventoryRevision, Is.EqualTo(1u));
        Assert.That(result.Rows.Single().ItemDefinitionId, Is.EqualTo(Gel));
        Assert.That(result.Rows.Single().Quantity, Is.EqualTo(3));
        Assert.That(Revision(character), Is.EqualTo(1));
        Assert.That(Held(character), Is.EqualTo(3));
        Assert.That(
            m_sql.Scalar(
                "SELECT count(*) FROM economy_ledger WHERE "
                + $"operation_id = '{drop}' AND actor_character_id = {character} AND operation_type = 'pickup' "
                + $"AND item_instance_id = {result.Rows.Single().Id} AND item_definition_id = '{Gel}' AND quantity_delta = 3 "
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

        InventoryResult full = Commit(character, refused, 1, maxRows: 1);
        InventoryResult added = Commit(character, Guid.NewGuid(), 1, maxRows: 2);
        InventoryResult merged = Commit(character, Guid.NewGuid(), 1, maxRows: 2);

        Assert.That(full.Status, Is.EqualTo(InventoryStatus.InventoryFull));
        Assert.That(LedgerEntries(refused), Is.EqualTo(0));
        Assert.That(added.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(merged.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(merged.Rows.Single().Quantity, Is.EqualTo(2));
    }

    [Test]
    public void Commit_OfADropAnotherCharacterHolds_ChangesNothing()
    {
        long first = NewCharacter();
        long second = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(first, drop, 1);

        InventoryResult result = Commit(second, drop, 1);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.TakenByOther));
        Assert.That(Held(second), Is.EqualTo(0));
        Assert.That(Revision(second), Is.EqualTo(0));
        Assert.That(LedgerEntries(drop), Is.EqualTo(1));
    }

    [Test]
    public void Commit_OfAnItemAlreadyHeld_MergesIntoItsRow()
    {
        long character = NewCharacter();
        long row = Commit(character, Guid.NewGuid(), 2).Rows.Single().Id;

        InventoryResult result = Commit(character, Guid.NewGuid(), 5);

        Assert.That(result.Rows.Single().Id, Is.EqualTo(row));
        Assert.That(result.Rows.Single().Quantity, Is.EqualTo(7));
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

        InventoryResult again = Commit(character, drop, 4);

        Assert.That(again.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(again.InventoryRevision, Is.EqualTo(1u));
        Assert.That(again.Rows.Single().Quantity, Is.EqualTo(4));
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

        Assert.That(Find(NewCharacter(), drop)!.Status, Is.EqualTo(InventoryStatus.TakenByOther));
    }

    [Test]
    public void Find_ForTheCharacterThatHoldsTheDrop_ReportsTheInventoryNow()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();
        Commit(character, drop, 2);
        Commit(character, Guid.NewGuid(), 3);

        InventoryResult? found = Find(character, drop);

        Assert.That(found!.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(found.InventoryRevision, Is.EqualTo(2u));
        Assert.That(found.Rows.Single().Quantity, Is.EqualTo(5));
    }

    [Test]
    public void Find_WhileTheCommitIsStillOpen_WaitsForItInsteadOfAnsweringNotCommitted()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();
        using var commit = new NpgsqlConnection(m_database.ConnectionString);
        commit.Open();
        using NpgsqlTransaction transaction = commit.BeginTransaction();
        using (var locking = new NpgsqlCommand(
                   $"SELECT id FROM characters WHERE id = {character} FOR UPDATE; "
                   + Sql.LedgerInsert(drop, character, "pickup"),
                   commit,
                   transaction))
        {
            locking.ExecuteNonQuery();
        }

        Task<InventoryResult?> lookup = FindInBackground(character, drop);
        bool isAnsweredEarly = lookup.Wait(500);
        transaction.Commit();

        Assert.That(isAnsweredEarly, Is.False, "the lookup waited for the character lock");
        Assert.That(lookup.GetAwaiter().GetResult()!.Status, Is.EqualTo(InventoryStatus.Committed));
    }

    // The lookup of Persistence §5: the ledger's row and the ones the caller names, each as it is now with its slot.
    // An emptied row, gone from the table, reports a quantity of 0 under the item the ledger names.
    [Test]
    public void Find_WithRowsToReport_AnswersEachAsItIsNow_WithItsSlot_AndAnEmptiedRowAtZero()
    {
        long character = NewCharacter();
        var drop = Guid.NewGuid();
        long gel = Commit(character, drop, 3).Rows.Single().Id;
        long equipped = m_sql.InsertItem(character, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", equipped));
        m_sql.Execute($"DELETE FROM inventory_items WHERE id = {gel}");

        InventoryResult found = Find(character, drop, new[] { equipped, gel })!;

        Assert.That(found.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(found.InventoryRevision, Is.EqualTo((uint)Revision(character)));
        Assert.That(
            found.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity, row.EquippedSlot)),
            Is.EqualTo(new[] { (gel, Gel, 0, (string?)null), (equipped, Gel, 1, "Weapon") }),
            "the ledger's row first, once");
    }
}
}
