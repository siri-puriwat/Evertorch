using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Pickups committed at the same moment on separate connections (Persistence §11: "Pickup commits inventory plus
///     ledger exactly once under duplicate requests"; "Two concurrent pickup attempts result in one durable owner").
/// </summary>
[TestFixture]
public sealed class PickupConcurrencyTests
{
    private const string Gel = "item.material.slime_gel";
    private const int Rounds = 10;

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
        return m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Race"));
    }

    // Each commit runs on its own thread and connection, released together.
    private PickupResult[] CommitTogether(params (long Character, Guid Drop)[] pickups)
    {
        using var start = new Barrier(pickups.Length);
        Task<PickupResult>[] running = pickups
            .Select(pickup => Task.Run(() =>
            {
                start.SignalAndWait();
                return m_store.CommitPickupAsync(
                        new PickupCommit(pickup.Drop, pickup.Character, Gel, 2, 999, 100, Now),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }))
            .ToArray();
        return Task.WhenAll(running).GetAwaiter().GetResult();
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

    [Test]
    public void OneCharacter_CommittingManyDropsAtOnce_TakesEachInTurn()
    {
        long character = NewCharacter();
        (long, Guid)[] pickups = Enumerable.Range(0, 8).Select(_ => (character, Guid.NewGuid())).ToArray();

        PickupResult[] results = CommitTogether(pickups);

        Assert.That(results.Select(result => result.Status), Is.All.EqualTo(PickupStatus.Committed));
        Assert.That(results.Select(result => result.InventoryRevision), Is.EquivalentTo(Enumerable.Range(1, 8)
            .Select(revision => (uint)revision)));
        Assert.That(Held(character), Is.EqualTo(16));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE character_id = {character}"),
            Is.EqualTo(1), "the stacks merged into one row under the lock");
    }

    [Test]
    public void OneCharacter_CommittingOneDropTwiceAtOnce_GainsItOnce()
    {
        for (int round = 0; round < Rounds; round++)
        {
            long character = NewCharacter();
            var drop = Guid.NewGuid();

            PickupResult[] results = CommitTogether((character, drop), (character, drop));

            Assert.That(results.Select(result => result.Status), Is.All.EqualTo(PickupStatus.Committed));
            Assert.That(results.Select(result => result.InventoryRevision), Is.All.EqualTo(1u));
            Assert.That(Held(character), Is.EqualTo(2));
            Assert.That(LedgerEntries(drop), Is.EqualTo(1));
        }
    }

    [Test]
    public void TwoCharacters_CommittingOneDropAtOnce_LeaveExactlyOneOwner()
    {
        for (int round = 0; round < Rounds; round++)
        {
            long first = NewCharacter();
            long second = NewCharacter();
            var drop = Guid.NewGuid();

            PickupResult[] results = CommitTogether((first, drop), (second, drop));

            Assert.That(results.Count(result => result.Status == PickupStatus.Committed), Is.EqualTo(1));
            Assert.That(results.Count(result => result.Status == PickupStatus.TakenByOther), Is.EqualTo(1));
            Assert.That(Held(first) + Held(second), Is.EqualTo(2));
            Assert.That(LedgerEntries(drop), Is.EqualTo(1));
            long owner = results[0].Status == PickupStatus.Committed ? first : second;
            long other = owner == first ? second : first;
            Assert.That(m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {owner}"), Is.EqualTo(1));
            Assert.That(m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {other}"), Is.Zero);
        }
    }
}
}
