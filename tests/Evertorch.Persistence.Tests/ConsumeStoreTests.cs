using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A use commits like a pickup (Persistence §5): one transaction under the character's lock, a <c>consume</c> ledger
///     row of −1, the revision up by one, and a repeat answered from the ledger. The last unit deletes its row, which
///     the answer and a later lookup report at quantity 0 under the ledger's item.
/// </summary>
[TestFixture]
public sealed class ConsumeStoreTests
{
    private const string Potion = "item.consumable.minor_health";
    private const int MaxRows = 100;

    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

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
        return m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Drink"));
    }

    private long GivePotions(long character, int quantity)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(Guid.NewGuid(), character, Potion, quantity, 50, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Rows.Single()
            .Id;
    }

    private InventoryResult Consume(long character, long row, Guid? operation = null)
    {
        return m_store.CommitConsumeAsync(
                new ConsumeCommit(operation ?? Guid.NewGuid(), character, row, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private long QuantityOf(long row)
    {
        return m_sql.Scalar($"SELECT coalesce(max(quantity), 0) FROM inventory_items WHERE id = {row}");
    }

    private static (long, string, int)[] Rows(InventoryResult result)
    {
        return result.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity)).ToArray();
    }

    [Test]
    public void Consume_OfAnotherCharactersRow_OrOfNoRow_ChangesNothing()
    {
        long character = NewCharacter();
        long other = NewCharacter();
        long theirs = GivePotions(other, 2);
        long revision = Revision(character);

        InventoryResult stolen = Consume(character, theirs);
        InventoryResult missing = Consume(character, 999_999_999);

        Assert.That(new[] { stolen.Status, missing.Status }, Is.All.EqualTo(InventoryStatus.Refused));
        Assert.That(QuantityOf(theirs), Is.EqualTo(2));
        Assert.That(Revision(character), Is.EqualTo(revision));
    }

    [Test]
    public void Consume_OfTheLastUnit_DeletesTheRow_AndTheLookupReportsItAtZeroUnderItsItem()
    {
        long character = NewCharacter();
        long potion = GivePotions(character, 1);
        var operation = Guid.NewGuid();

        InventoryResult result = Consume(character, potion, operation);
        InventoryResult? found = m_store
            .FindOperationAsync(operation, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(Rows(result), Is.EqualTo(new[] { (potion, Potion, 0) }));
        Assert.That(Rows(found!), Is.EqualTo(Rows(result)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE id = {potion}"), Is.Zero);
    }

    [Test]
    public void Consume_TakesOneUnit_WithOneLedgerRowOfMinusOneAndOneRevision()
    {
        long character = NewCharacter();
        long potions = GivePotions(character, 3);
        long revision = Revision(character);
        var operation = Guid.NewGuid();

        InventoryResult result = Consume(character, potions, operation);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(result), Is.EqualTo(new[] { (potions, Potion, 2) }));
        Assert.That(result.InventoryRevision, Is.EqualTo((uint)(revision + 1)));
        Assert.That(QuantityOf(potions), Is.EqualTo(2));
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE operation_id = '{operation}' AND operation_type = 'consume' "
                + $"AND item_instance_id = {potions} AND quantity_delta = -1 AND currency_delta = 0"),
            Is.EqualTo(1));
    }

    [Test]
    public void Consumes_OfTheLastUnitAtOnce_CommitOnce_AndRefuseTheOther()
    {
        long character = NewCharacter();
        long potion = GivePotions(character, 1);
        long revision = Revision(character);

        Task<InventoryResult>[] running = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => Consume(character, potion)))
            .ToArray();
        Task.WaitAll(running);

        InventoryStatus[] statuses = running.Select(task => task.Result.Status).OrderBy(status => status).ToArray();
        Assert.That(statuses, Is.EqualTo(new[] { InventoryStatus.Committed, InventoryStatus.Refused }));
        Assert.That(Revision(character), Is.EqualTo(revision + 1));
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
                + "AND operation_type = 'consume'"),
            Is.EqualTo(1));
    }

    [Test]
    public void Repeats_OfOneOperation_TakeOneUnit()
    {
        long character = NewCharacter();
        long potions = GivePotions(character, 3);
        long revision = Revision(character);
        var operation = Guid.NewGuid();

        InventoryResult first = Consume(character, potions, operation);
        InventoryResult again = Consume(character, potions, operation);

        Assert.That(new[] { first.Status, again.Status }, Is.All.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(again), Is.EqualTo(Rows(first)));
        Assert.That(QuantityOf(potions), Is.EqualTo(2));
        Assert.That(Revision(character), Is.EqualTo(revision + 1));
    }
}
}
