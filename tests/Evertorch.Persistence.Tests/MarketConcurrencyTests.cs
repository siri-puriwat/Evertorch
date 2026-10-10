using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Purchases and collections committed at the same moment on separate connections over one listing (Persistence
///     §5, §11): the item's price row puts them in an order, so the last unit goes to one buyer alone, and a collection
///     racing a purchase leaves every unit and every coin in one place. Gloom Dust lists at a base of 28.
/// </summary>
[TestFixture]
public sealed class MarketConcurrencyTests
{
    private const string Dust = "item.material.gloom_dust";
    private const int DustStack = 999;
    private const int BagRows = 100;
    private const long Period = 86_400_000;
    private const int Rounds = 10;

    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemMarket DustTerms = new(28, 14, 280);

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

    private long NewCharacter(long coins)
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Race"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        return character;
    }

    private long Held(string item, params long[] characters)
    {
        return m_sql.Scalar(
            "SELECT coalesce(sum(quantity), 0) FROM inventory_items "
            + $"WHERE item_definition_id = '{item}' AND character_id IN ({string.Join(", ", characters)})");
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    // Each round lists an item of its own, 98 coins left to its seller after the fee for ten, 2.
    private (string Item, long Seller, long Listing) Listed(int units)
    {
        string item = $"{Dust}_{Guid.NewGuid():N}";
        long seller = NewCharacter(100);
        long row = m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({seller}, '{item}', {units}, 0, 0) RETURNING id");
        MarketResult listed = m_store.CommitMarketListAsync(
                new MarketListCommit(
                    Guid.NewGuid(),
                    seller,
                    row,
                    item,
                    units,
                    DustTerms,
                    Period,
                    3 * Period,
                    MarketTerms.MaxListings,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(listed.Status, Is.EqualTo(InventoryStatus.Committed), "listed");
        return (item, seller, listed.ListingId);
    }

    private static Task<T> Released<T>(Barrier start, Func<Task<T>> commit)
    {
        return Task.Run(() =>
        {
            start.SignalAndWait();
            return commit().GetAwaiter().GetResult();
        });
    }

    private Task<MarketResult> Buy(Barrier start, long buyer, string item, int quantity)
    {
        var buy = new MarketBuyCommit(
            Guid.NewGuid(),
            buyer,
            item,
            quantity,
            28,
            DustStack,
            BagRows,
            DustTerms,
            Period,
            Now);
        return Released(start, () => m_store.CommitMarketBuyAsync(buy, CancellationToken.None));
    }

    // A collection and a purchase of four of its ten units at once: whichever the price row lets in first decides
    // what each gets, and the units and coins all land somewhere, once.
    [Test]
    public void APurchaseAndACollection_RacingOverOneListing_KeepEveryUnitAndCoin()
    {
        for (int round = 0; round < Rounds; round++)
        {
            (string item, long seller, long listing) = Listed(10);
            long buyer = NewCharacter(500);
            using var start = new Barrier(2);
            var collect = new MarketCollectCommit(
                Guid.NewGuid(),
                seller,
                listing,
                new Dictionary<string, int> { [item] = DustStack },
                new Dictionary<string, ItemMarket> { [item] = DustTerms },
                BagRows,
                Period,
                Now);

            Task<MarketResult> bought = Buy(start, buyer, item, 4);
            Task<MarketResult> collected = Released(
                start,
                () => m_store.CommitMarketCollectAsync(collect, CancellationToken.None));
            Task.WhenAll(bought, collected).GetAwaiter().GetResult();

            bool isBought = bought.Result.Status == InventoryStatus.Committed;
            Assert.That(collected.Result.Status, Is.EqualTo(InventoryStatus.Committed), $"round {round}: collected");
            Assert.That(
                bought.Result.Status,
                Is.EqualTo(isBought ? InventoryStatus.Committed : InventoryStatus.BoardChanged),
                $"round {round}: a purchase after the collection finds nothing");
            Assert.That(
                (Held(item, buyer), Held(item, seller)),
                Is.EqualTo(isBought ? (4L, 6L) : (0L, 10L)),
                $"round {round}: every unit in one bag");
            Assert.That(
                (Coins(buyer), Coins(seller)),
                Is.EqualTo(isBought ? (388L, 98L + 112 - 10) : (500L, 98L)),
                $"round {round}: 112 paid, 10 of it the tax");
            Assert.That(
                m_sql.Scalar($"SELECT count(*) FROM market_listings WHERE id = {listing}"),
                Is.Zero,
                $"round {round}: the listing ended");
        }
    }

    // One unit left and two buyers of other accounts at once: one buys it, the other finds too few left and pays
    // nothing.
    [Test]
    public void TwoBuyers_RacingForTheLastUnit_OneTakesIt()
    {
        for (int round = 0; round < Rounds; round++)
        {
            (string item, _, long listing) = Listed(1);
            long first = NewCharacter(100);
            long second = NewCharacter(100);
            using var start = new Barrier(2);

            MarketResult[] results = Task.WhenAll(Buy(start, first, item, 1), Buy(start, second, item, 1))
                .GetAwaiter()
                .GetResult();

            Assert.That(
                results.Select(result => result.Status).OrderBy(status => status),
                Is.EqualTo(new[] { InventoryStatus.Committed, InventoryStatus.BoardChanged }.OrderBy(status => status)),
                $"round {round}");
            Assert.That(Held(item, first, second), Is.EqualTo(1), $"round {round}: the unit exists once");
            Assert.That(Coins(first) + Coins(second), Is.EqualTo(200 - 28), $"round {round}: paid once");
            Assert.That(
                m_sql.Text($"SELECT units_left || ' ' || gross_coins FROM market_listings WHERE id = {listing}"),
                Is.EqualTo("0 28"),
                $"round {round}");
            Assert.That(
                m_sql.Scalar($"SELECT count(*) FROM market_sales WHERE listing_id = {listing}"),
                Is.EqualTo(1),
                $"round {round}: one sale");
        }
    }
}
}
