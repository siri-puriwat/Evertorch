using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Trades and deposits committed at the same moment on separate connections over one character's row (Persistence
///     §5, §11): the locks put them in an order, and whichever comes second finds the row gone, so the row has one owner
///     and nothing is made twice.
/// </summary>
[TestFixture]
public sealed class TradeConcurrencyTests
{
    private const string Gel = "item.material.slime_gel";
    private const int GelStack = 999;
    private const int Rounds = 10;

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int> { [Gel] = GelStack };

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

    private long NewCharacter(long coins = 0)
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Race"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        return character;
    }

    private long Give(long character, int quantity)
    {
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{Gel}', {quantity}, 0, 0) RETURNING id");
    }

    private long GelHeld(params long[] characters)
    {
        return m_sql.Scalar(
            "SELECT coalesce(sum(quantity), 0) FROM inventory_items "
            + $"WHERE item_definition_id = '{Gel}' AND character_id IN ({string.Join(", ", characters)})");
    }

    private long GelStored()
    {
        return m_sql.Scalar($"SELECT coalesce(sum(quantity), 0) FROM storage_items WHERE item_definition_id = '{Gel}'");
    }

    private Task<T> Released<T>(Barrier start, Func<Task<T>> commit)
    {
        return Task.Run(() =>
        {
            start.SignalAndWait();
            return commit().GetAwaiter().GetResult();
        });
    }

    private Task<TradeResult> TradeAll(Barrier start, long giver, long row, int quantity, long taker)
    {
        TraderOffer gives = new(giver, new[] { new TradeLine(row, quantity) }, 0);
        TraderOffer takes = new(taker, Array.Empty<TradeLine>(), 0);
        TradeCommit commit = giver < taker
            ? new TradeCommit(Guid.NewGuid(), gives, takes, Limits, 100, Now)
            : new TradeCommit(Guid.NewGuid(), takes, gives, Limits, 100, Now);
        return Released(start, () => m_store.CommitTradeAsync(commit, CancellationToken.None));
    }

    // A trade of a row and a deposit of the same row at once: one of them moves it, the other is refused, and the
    // gel is either in the partner's bag or in storage, never both.
    [Test]
    public void ATradeAndADeposit_RacingOverOneRow_MoveItOnce()
    {
        for (int round = 0; round < Rounds; round++)
        {
            long giver = NewCharacter(20);
            long taker = NewCharacter();
            long row = Give(giver, 10);
            long storedBefore = GelStored();
            using var start = new Barrier(2);

            Task<TradeResult> trade = TradeAll(start, giver, row, 10, taker);
            Task<StorageResult> deposit = Released(
                start,
                () => m_store.CommitStorageDepositAsync(
                    new StorageDepositCommit(Guid.NewGuid(), giver, row, 10, 20, GelStack, 300, Now),
                    CancellationToken.None));
            Task.WhenAll(trade, deposit).GetAwaiter().GetResult();

            bool isTraded = trade.Result.Status == TradeStatus.Committed;
            bool isStored = deposit.Result.Status == InventoryStatus.Committed;
            Assert.That(isTraded ^ isStored, Is.True, $"round {round}: one of the two, never both");
            Assert.That(
                (GelHeld(taker), GelStored() - storedBefore, GelHeld(giver)),
                Is.EqualTo(isTraded ? (10L, 0L, 0L) : (0L, 10L, 0L)),
                $"round {round}");
            Assert.That(
                m_sql.Scalar($"SELECT currency FROM characters WHERE id = {giver}"),
                Is.EqualTo(isStored ? 0 : 20),
                $"round {round}: the fee only for a deposit that happened");
        }
    }

    // One character offers its whole stack of gel to two others at once: one trade takes it, the other finds the row
    // gone and is refused; the gel exists once.
    [Test]
    public void TwoTrades_RacingOverOneRow_MoveItOnce()
    {
        for (int round = 0; round < Rounds; round++)
        {
            long giver = NewCharacter();
            long first = NewCharacter();
            long second = NewCharacter();
            long row = Give(giver, 10);
            using var start = new Barrier(2);

            TradeResult[] results = Task.WhenAll(
                    TradeAll(start, giver, row, 10, first),
                    TradeAll(start, giver, row, 10, second))
                .GetAwaiter()
                .GetResult();

            Assert.That(
                results.Select(result => result.Status).OrderBy(status => status),
                Is.EqualTo(new[] { TradeStatus.Committed, TradeStatus.Refused }.OrderBy(status => status)),
                $"round {round}");
            Assert.That(GelHeld(giver, first, second), Is.EqualTo(10), $"round {round}: the gel exists once");
            Assert.That(
                m_sql.Scalar($"SELECT count(*) FROM trades WHERE {giver} IN (first_character_id, second_character_id)"),
                Is.EqualTo(1),
                $"round {round}");
        }
    }
}
}
