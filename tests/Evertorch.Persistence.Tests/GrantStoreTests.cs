using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A boss's prize granted in the store (Persistence §5; owner decision 8 of the pre-Milestone-13 review): the item
///     and a <c>boss_reward</c> ledger row in one transaction, for no coins, once under repeats and lost answers, and
///     never past the stack limit or the row limit; an operation ID another character's pickup took grants nothing.
/// </summary>
[TestFixture]
public sealed class GrantStoreTests
{
    private const string Mantle = "item.armor.monarch_mantle";
    private const string Jelly = "item.material.monarch_jelly";
    private const string Shell = "item.material.crawler_shell";
    private const int JellyStack = 999;
    private const int MaxRows = 100;

    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

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
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Mvp"));
        m_sql.Execute($"UPDATE characters SET currency = 75 WHERE id = {character}");
        return character;
    }

    private long Give(long character, string item, int quantity)
    {
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{item}', {quantity}, 0, 0) RETURNING id");
    }

    private void GiveRows(long character, int rows)
    {
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {character}, '{Shell}', 1, 0, 0 FROM generate_series(1, {rows})");
    }

    private InventoryResult Grant(long character, string item, int quantity, int stack, Guid? operation = null)
    {
        return m_store.CommitGrantAsync(
                new GrantCommit(operation ?? Guid.NewGuid(), character, item, quantity, stack, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private long Rows(long character)
    {
        return m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE character_id = {character}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || quantity_delta || ' ' || currency_delta, ', ' "
            + $"ORDER BY id), '') FROM economy_ledger WHERE actor_character_id = {character}");
    }

    [Test]
    public void Grant_IntoANewRow_RaisesTheRevision_KeepsTheCoins_AndRecordsABossReward()
    {
        long character = NewCharacter();

        InventoryResult granted = Grant(character, Mantle, 1, 1);

        StoredItem row = granted.Rows.Single();
        Assert.That(
            (granted.Status, granted.Coins, granted.InventoryRevision),
            Is.EqualTo((InventoryStatus.Committed, 75L, 1u)));
        Assert.That((row.ItemDefinitionId, row.Quantity), Is.EqualTo((Mantle, 1)));
        Assert.That(Ledger(character), Is.EqualTo("boss_reward 1 0"));
    }

    [Test]
    public void Grant_IntoAStack_MergesIntoItsRow_AndAStackLimitOfOneTakesARowOfItsOwn()
    {
        long character = NewCharacter();
        long jelly = Give(character, Jelly, 2);

        InventoryResult merged = Grant(character, Jelly, 5, JellyStack);
        Grant(character, Mantle, 1, 1);
        Grant(character, Mantle, 1, 1);

        Assert.That(merged.Rows.Select(row => (row.Id, row.Quantity)), Is.EqualTo(new[] { (jelly, 7) }));
        Assert.That(Rows(character), Is.EqualTo(3), "the jelly's row and a row for each mantle");
    }

    [Test]
    public void Grant_PastTheStackLimit_OrWithNoFreeRow_IsFull_AndChangesNothing()
    {
        long atTheLimit = NewCharacter();
        Give(atTheLimit, Jelly, JellyStack - 1);
        long noRow = NewCharacter();
        GiveRows(noRow, MaxRows);

        InventoryResult pastTheLimit = Grant(atTheLimit, Jelly, 2, JellyStack);
        InventoryResult withoutARow = Grant(noRow, Mantle, 1, 1);

        Assert.That(
            (pastTheLimit.Status, withoutARow.Status),
            Is.EqualTo((InventoryStatus.InventoryFull, InventoryStatus.InventoryFull)));
        Assert.That((Ledger(atTheLimit), Ledger(noRow)), Is.EqualTo((string.Empty, string.Empty)));
        Assert.That(Rows(noRow), Is.EqualTo(MaxRows));
    }

    // A prize that fell at the player's feet carries the grant's operation ID as its drop ID, so once anyone picked
    // it up, the grant can no longer be made (Gameplay Systems §11).
    [Test]
    public void Grant_UnderAnOperationAnotherCharactersPickupTook_IsTakenByOther_AndAddsNothing()
    {
        long winner = NewCharacter();
        long picker = NewCharacter();
        var operation = Guid.NewGuid();
        m_store.CommitPickupAsync(
                new PickupCommit(operation, picker, Mantle, 1, 1, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        InventoryResult granted = Grant(winner, Mantle, 1, 1, operation);

        Assert.That(granted.Status, Is.EqualTo(InventoryStatus.TakenByOther));
        Assert.That((Rows(winner), Ledger(winner)), Is.EqualTo((0L, string.Empty)));
    }

    [Test]
    public void Repeats_OfOneGrant_AddOnce_AndTheLookupFindsIt()
    {
        long character = NewCharacter();
        var operation = Guid.NewGuid();

        Grant(character, Jelly, 5, JellyStack, operation);
        InventoryResult repeat = Grant(character, Jelly, 5, JellyStack, operation);
        InventoryResult? found = m_store
            .FindOperationAsync(operation, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((repeat.Status, repeat.Rows.Single().Quantity), Is.EqualTo((InventoryStatus.Committed, 5)));
        Assert.That((found!.Status, found.Rows.Single().Quantity), Is.EqualTo((InventoryStatus.Committed, 5)));
        Assert.That(Ledger(character), Is.EqualTo("boss_reward 5 0"));
    }
}
}
