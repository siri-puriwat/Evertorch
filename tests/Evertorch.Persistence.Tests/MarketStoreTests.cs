using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     The market board in the store (Persistence §5; owner decisions 1–5 of the pre-Milestone-15 review): a listing
///     takes a bag row's units and a fee at the item's settled price, a collection pays a listing's sales less the tax
///     and gives back its unsold units, each in one transaction with one ledger row; a repeat and the lookup answer from
///     the ledger. Gloom Dust lists at a base of 28 between 14 and 280; a period is a day.
/// </summary>
[TestFixture]
public sealed class MarketStoreTests
{
    private const string Dust = "item.material.gloom_dust";
    private const string Sword = "item.weapon.iron_sword";
    private const int DustStack = 999;
    private const int BagRows = 100;
    private const long Period = 86_400_000;
    private const long Duration = 3 * Period;

    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemMarket DustTerms = new(28, 14, 280);
    private static readonly ItemMarket SwordTerms = new(120, 60, 1200);

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

    private long NewCharacter(long coins, long? account = null)
    {
        long character = m_sql.InsertCharacter(account ?? m_sql.InsertAccount(), Sql.UniqueName("Mkt"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        return character;
    }

    private long Give(long character, string item, int quantity, int refine = 0, string? instance = null)
    {
        string data = instance == null ? "NULL" : $"'{instance}'::jsonb";
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"instance_data_json, version) VALUES ({character}, '{item}', {quantity}, {refine}, {data}, 0) "
            + "RETURNING id");
    }

    // Each test lists an item of its own, so the board's prices of one test never meet another's.
    private static string Fresh(string item)
    {
        return $"{item}_{Guid.NewGuid():N}";
    }

    private MarketResult List(
        long character,
        long row,
        string item,
        int quantity,
        DateTime? at = null,
        Guid? operation = null,
        ItemMarket? terms = null)
    {
        return m_store.CommitMarketListAsync(
                new MarketListCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    row,
                    item,
                    quantity,
                    terms ?? DustTerms,
                    Period,
                    Duration,
                    MarketTerms.MaxListings,
                    at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private MarketResult Collect(
        long character,
        long listing,
        IReadOnlyDictionary<string, int> stackLimits,
        DateTime? at = null,
        Guid? operation = null)
    {
        return m_store.CommitMarketCollectAsync(
                new MarketCollectCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    listing,
                    stackLimits,
                    stackLimits.Keys.ToDictionary(item => item, item => item.StartsWith(Sword, StringComparison.Ordinal)
                        ? SwordTerms
                        : DustTerms),
                    BagRows,
                    Period,
                    at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    // What a purchase leaves on a listing, written as one would until purchases exist in the store.
    private void Sell(long listing, int unitsSold, long gross)
    {
        m_sql.Execute(
            $"UPDATE market_listings SET units_left = units_left - {unitsSold}, gross_coins = {gross} WHERE id = {listing}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || quantity_delta || ' ' || currency_delta, ', ' "
            + $"ORDER BY id), '') FROM economy_ledger WHERE actor_character_id = {character}");
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private string Listing(long listing)
    {
        return m_sql.Text(
            "SELECT coalesce((SELECT units_listed || ' ' || units_left || ' ' || gross_coins || ' ' "
            + "|| to_char(expires_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI') FROM market_listings "
            + $"WHERE id = {listing}), 'gone')");
    }

    private string Price(string item)
    {
        return m_sql.Text(
            "SELECT price || ' ' || listed_ms || ' ' || coalesce(to_char(stock_until AT TIME ZONE 'UTC', "
            + $"'YYYY-MM-DD HH24:MI'), 'none') FROM market_prices WHERE item_definition_id = '{item}'");
    }

    private MarketResult Buy(
        long character,
        string item,
        int quantity,
        int priceLimit,
        DateTime? at = null,
        Guid? operation = null)
    {
        return m_store.CommitMarketBuyAsync(
                new MarketBuyCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    item,
                    quantity,
                    priceLimit,
                    DustStack,
                    BagRows,
                    DustTerms,
                    Period,
                    at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private StoredMarket Read(long character, string item, DateTime? at = null)
    {
        return m_store.ReadMarketAsync(
                new MarketRead(character, new Dictionary<string, ItemMarket> { [item] = DustTerms }, Period, at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private string Sales(long buyer)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(listing_id || ':' || units || ':' || gross_coins, ', ' ORDER BY listing_id), "
            + $"'') FROM market_sales WHERE buyer_character_id = {buyer}");
    }

    [Test]
    public void Buy_FillsTheOldestUnitsOfOtherAccounts_AtTheSettledPrice_AndRecordsEachSale()
    {
        string item = Fresh(Dust);
        long first = NewCharacter(100);
        long second = NewCharacter(100);
        long buyer = NewCharacter(1000);
        long older = List(first, Give(first, item, 10), item, 10).ListingId;
        long newer = List(second, Give(second, item, 5), item, 5, Now.AddHours(1)).ListingId;

        MarketResult bought = Buy(buyer, item, 12, 28, Now.AddHours(2));

        Assert.That(
            (bought.Status, bought.Coins, bought.CoinsMoved, bought.Quantity, bought.Rows.Single().Quantity),
            Is.EqualTo((InventoryStatus.Committed, 1000L - 336, 336L, 12, 12)));
        Assert.That(
            bought.Fills.Select(fill => (fill.ListingId, fill.SellerCharacterId, fill.Units, fill.Gross)),
            Is.EqualTo(new[] { (older, first, 10, 280L), (newer, second, 2, 56L) }),
            "the older listing first");
        Assert.That(Sales(buyer), Is.EqualTo($"{older}:10:280, {newer}:2:56"));
        Assert.That((Listing(older), Listing(newer)),
            Is.EqualTo(("10 0 280 2026-10-13 12:00", "5 3 56 2026-10-13 13:00")));
        Assert.That((Coins(first), Coins(second)), Is.EqualTo((98L, 99L)), "the sellers' coins untouched");
        Assert.That(Ledger(buyer), Is.EqualTo("market_buy 12 -336"));
        Assert.That(Price(item), Does.StartWith("28 "), "units were left, so no rise");
    }

    [Test]
    public void Buy_OfItsOwnAccountsStock_OrOfExpiredStock_IsRefused_AndTheBoardShowsNoStock()
    {
        string item = Fresh(Dust);
        long account = m_sql.InsertAccount();
        long seller = NewCharacter(100, account);
        long sameAccount = NewCharacter(100, account);
        long other = NewCharacter(100);
        List(seller, Give(seller, item, 5), item, 5);

        MarketResult own = Buy(sameAccount, item, 1, 28);
        MarketResult expired = Buy(other, item, 1, 28, Now.AddDays(3).AddSeconds(1));

        Assert.That((own.Status, expired.Status), Is.EqualTo((InventoryStatus.BoardChanged,
            InventoryStatus.BoardChanged)));
        Assert.That(Read(sameAccount, item).Entries.Single().Stock, Is.Zero, "its own account's stock is not for it");
        Assert.That(Read(other, item).Entries.Single().Stock, Is.EqualTo(5));
        Assert.That(Read(other, item, Now.AddDays(3).AddSeconds(1)).Entries.Single().Stock, Is.Zero, "expired");
        Assert.That(Sales(sameAccount) + Sales(other), Is.Empty);
    }

    [Test]
    public void Buy_Refusals_ChangeNothing()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long listing = List(seller, Give(seller, item, 5), item, 5).ListingId;
        long poor = NewCharacter(27);
        long full = NewCharacter(1000);
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {full}, '{Sword}', 1, 0, 0 FROM generate_series(1, {BagRows})");
        long buyer = NewCharacter(1000);

        MarketResult[] results =
        {
            Buy(buyer, item, 1, 27),
            Buy(buyer, item, 6, 28),
            Buy(poor, item, 1, 28),
            Buy(full, item, 1, 28)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.BoardChanged,
                InventoryStatus.BoardChanged,
                InventoryStatus.NotEnoughCoins,
                InventoryStatus.InventoryFull
            }),
            "a price past the limit, too few units, the coins, and the room");
        Assert.That(Listing(listing), Does.StartWith("5 5 0"));
        Assert.That(Ledger(buyer) + Ledger(poor) + Ledger(full), Is.Empty);
        Assert.That(Coins(buyer), Is.EqualTo(1000));
    }

    [Test]
    public void Buy_Repeated_BuysOnce_AndTheRepeatNamesItsSales()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long listing = List(seller, Give(seller, item, 10), item, 10).ListingId;
        long buyer = NewCharacter(1000);
        var operation = Guid.NewGuid();

        Buy(buyer, item, 4, 28, operation: operation);
        MarketResult repeat = Buy(buyer, item, 4, 28, operation: operation);

        Assert.That((repeat.Status, repeat.Coins, repeat.Quantity), Is.EqualTo((InventoryStatus.Committed, 888L, 4)));
        Assert.That(repeat.Fills.Single().ListingId, Is.EqualTo(listing));
        Assert.That(Listing(listing), Does.StartWith("10 6 112"));
        Assert.That(Ledger(buyer), Is.EqualTo("market_buy 4 -112"));
    }

    [Test]
    public void Buy_TheLastUnitsTheBuyerCouldTake_RaisesThePrice_AtMostOnceAPeriod()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(1000);
        long buyer = NewCharacter(10_000);
        List(seller, Give(seller, item, 5), item, 5);
        Buy(buyer, item, 5, 28);
        string risen = Price(item);
        List(seller, Give(seller, item, 5), item, 5, Now.AddHours(1));
        Buy(buyer, item, 5, 30, Now.AddHours(2));
        string held = Price(item);
        List(seller, Give(seller, item, 5), item, 5, Now.AddDays(1));
        MarketResult later = Buy(buyer, item, 5, 30, Now.AddDays(1).AddHours(1));

        Assert.That(risen, Is.EqualTo("30 0 none"), "28 rose a step; no units left anywhere");
        Assert.That(held, Is.EqualTo($"30 {3_600_000} none"), "an hour of stock, and no second rise within a day");
        Assert.That((later.Status, Price(item)), Is.EqualTo((InventoryStatus.Committed, "33 0 none")),
            "a day after the first rise");
    }

    [Test]
    public void Collect_OfAListingStillSelling_EndsIt_AndThePriceStopsCountingItsStock()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long first = List(seller, Give(seller, item, 5), item, 5).ListingId;
        long second = List(seller, Give(seller, item, 5), item, 5, Now.AddHours(1)).ListingId;

        MarketResult collected = Collect(seller, second, new Dictionary<string, int> { [item] = DustStack },
            Now.AddHours(2));

        Assert.That((collected.Status, collected.Quantity, collected.CoinsMoved),
            Is.EqualTo((InventoryStatus.Committed, 5, 0L)));
        Assert.That(Listing(second), Is.EqualTo("gone"));
        Assert.That(Listing(first), Does.StartWith("5 5 0"));
        Assert.That(Price(item), Is.EqualTo($"28 {2 * 3_600_000} 2026-10-13 12:00"),
            "two hours listed; stock now ends with the first listing");
    }

    [Test]
    public void Collect_OfAnItemTheContentLost_IsRefused_NamingIt()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long listing = List(seller, Give(seller, item, 3), item, 3).ListingId;

        MarketResult refused = Collect(seller, listing, new Dictionary<string, int> { [Sword] = 1 });

        Assert.That((refused.Status, refused.ItemDefinitionId), Is.EqualTo((InventoryStatus.Refused, item)));
        Assert.That(Listing(listing), Does.StartWith("3 3 0"));
    }

    [Test]
    public void Collect_PastTheCoinCap_OrWithoutRoom_OrOfAnotherOrAGoneListing_MovesNothing()
    {
        string item = Fresh(Dust);
        long rich = NewCharacter(100);
        long sold = List(rich, Give(rich, item, 10), item, 10).ListingId;
        Sell(sold, 10, 120);
        m_sql.Execute($"UPDATE characters SET currency = {1_000_000_000 - 50} WHERE id = {rich}");
        long full = NewCharacter(100);
        long kept = List(full, Give(full, Sword, 1), Sword, 1, terms: SwordTerms).ListingId;
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {full}, '{Dust}', 1, 0, 0 FROM generate_series(1, {BagRows})");
        long other = NewCharacter(100);
        var limits = new Dictionary<string, int> { [item] = DustStack, [Sword] = 1 };

        MarketResult[] results =
        {
            Collect(rich, sold, limits),
            Collect(full, kept, limits),
            Collect(other, sold, limits),
            Collect(other, long.MaxValue, limits)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.CoinCapReached,
                InventoryStatus.InventoryFull,
                InventoryStatus.BoardChanged,
                InventoryStatus.BoardChanged
            }),
            "the cap, a full bag, another's listing, and none");
        Assert.That((Listing(sold), Listing(kept)),
            Is.EqualTo(("10 0 120 2026-10-13 12:00", "1 1 0 2026-10-13 12:00")));
        Assert.That(Coins(rich), Is.EqualTo(1_000_000_000L - 50));
        Assert.That(Ledger(other), Is.Empty);
    }

    [Test]
    public void Collect_PaysTheSalesLessTheTax_AndGivesBackTheUnitsLeft()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long dust = Give(seller, item, 10);
        long listing = List(seller, dust, item, 10).ListingId;
        Sell(listing, 4, 120);

        MarketResult collected = Collect(seller, listing, new Dictionary<string, int> { [item] = DustStack });

        Assert.That(
            (collected.Status, collected.Coins, collected.CoinsMoved, collected.Quantity, collected.ListingId),
            Is.EqualTo((InventoryStatus.Committed, 100L - 2 + 110, 110L, 6, listing)));
        Assert.That((collected.Rows.Single().ItemDefinitionId, collected.Rows.Single().Quantity),
            Is.EqualTo((item, 6)));
        Assert.That(collected.InventoryRevision, Is.EqualTo(2u), "the listing, then the collection");
        Assert.That(Listing(listing), Is.EqualTo("gone"));
        Assert.That(Ledger(seller), Is.EqualTo("market_list -10 -2, market_collect 6 110"));
        Assert.That(
            m_sql.Text(
                "SELECT metadata_json ->> 'gross' || ' ' || (metadata_json ->> 'tax') FROM economy_ledger "
                + $"WHERE actor_character_id = {seller} AND operation_type = 'market_collect'"),
            Is.EqualTo("120 10"));
    }

    [Test]
    public void List_AtTheItemsSettledPrice_PaysItsFee()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        List(seller, Give(seller, item, 1), item, 1);

        MarketResult later = List(seller, Give(seller, item, 100), item, 100, Now.AddDays(2.5));

        Assert.That(later.CoinsMoved, Is.EqualTo(24L), "a price fallen twice, 28 to 24, for a hundred");
        Assert.That(Price(item), Is.EqualTo($"24 {Period / 2} 2026-10-16 00:00"), "half a day carried");
    }

    [Test]
    public void List_OfARowWhole_TakesTheFee_DeletesTheRow_AndMakesTheListingAndThePrice()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long dust = Give(seller, item, 10);

        MarketResult listed = List(seller, dust, item, 10);

        Assert.That(
            (listed.Status, listed.Coins, listed.CoinsMoved, listed.Quantity, listed.InventoryRevision),
            Is.EqualTo((InventoryStatus.Committed, 98L, 2L, 10, 1u)),
            "a fee of 1 % of 280, 2 coins");
        Assert.That((listed.Rows.Single().Id, listed.Rows.Single().Quantity), Is.EqualTo((dust, 0)), "emptied");
        Assert.That(Listing(listed.ListingId), Is.EqualTo("10 10 0 2026-10-13 12:00"));
        Assert.That(Price(item), Is.EqualTo("28 0 2026-10-13 12:00"), "at its base, stock until the expiry");
        Assert.That(Ledger(seller), Is.EqualTo("market_list -10 -2"));
        Assert.That(
            m_sql.Scalar(
                $"SELECT (metadata_json ->> 'listing')::bigint FROM economy_ledger WHERE actor_character_id = {seller}"),
            Is.EqualTo(listed.ListingId),
            "the ledger names the listing");
    }

    [Test]
    public void List_PartOfARow_KeepsTheRest_AndARepeatListsOnce()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long dust = Give(seller, item, 10);
        var operation = Guid.NewGuid();

        MarketResult first = List(seller, dust, item, 4, operation: operation);
        MarketResult repeat = List(seller, dust, item, 4, operation: operation);

        Assert.That((repeat.Status, repeat.ListingId, repeat.Coins), Is.EqualTo((InventoryStatus.Committed,
            first.ListingId, 99L)));
        Assert.That(repeat.Rows.Single().Quantity, Is.EqualTo(6));
        Assert.That(Ledger(seller), Is.EqualTo("market_list -4 -1"));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM market_listings WHERE seller_character_id = {seller}"),
            Is.EqualTo(1));
    }

    [Test]
    public void List_Refusals_ChangeNothing()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long dust = Give(seller, item, 5);
        long worn = Give(seller, Sword, 1);
        m_sql.Execute(Sql.EquipmentInsert(seller, "Weapon", worn));
        long refined = Give(seller, Sword, 1, 3);
        long carded = Give(seller, Sword, 1, 0, "{\"cards\":[]}");
        long poor = NewCharacter(0);
        long poorDust = Give(poor, item, 5);
        long busy = NewCharacter(100);
        for (int index = 0; index < MarketTerms.MaxListings; index++)
        {
            List(busy, Give(busy, item, 1), item, 1);
        }

        MarketResult[] results =
        {
            List(seller, dust, item, 6),
            List(seller, dust, Sword, 1, terms: SwordTerms),
            List(seller, worn, Sword, 1, terms: SwordTerms),
            List(seller, refined, Sword, 1, terms: SwordTerms),
            List(seller, carded, Sword, 1, terms: SwordTerms),
            List(NewCharacter(100), dust, item, 1),
            List(poor, poorDust, item, 1),
            List(busy, Give(busy, item, 1), item, 1)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.NotEnoughCoins,
                InventoryStatus.ListingCapReached
            }),
            "more than held, another item, worn, refined, instance data, another's row, the fee, and a ninth");
        Assert.That((Ledger(seller), Ledger(poor), Coins(seller)), Is.EqualTo((string.Empty, string.Empty, 100L)));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM market_listings WHERE seller_character_id = {busy}"),
            Is.EqualTo(MarketTerms.MaxListings));
    }

    [Test]
    public void Lookup_AfterALostAnswer_FindsTheListing_AndTheBoardsItemsCountAtStartup()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long dust = Give(seller, item, 10);
        var operation = Guid.NewGuid();
        List(seller, dust, item, 10, operation: operation);

        InventoryResult? found = m_store.FindOperationAsync(operation, seller, Array.Empty<long>(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((found!.Status, found.Coins, found.InventoryRevision), Is.EqualTo((InventoryStatus.Committed,
            98L, 1u)));
        Assert.That((found.Rows.Single().Id, found.Rows.Single().ItemDefinitionId, found.Rows.Single().Quantity),
            Is.EqualTo((dust, item, 0)));
        Assert.That(m_store.ListStoredDefinitionIdsAsync(CancellationToken.None).GetAwaiter().GetResult(),
            Has.Member(item), "a listed item, gone from every bag");
    }

    [Test]
    public void Read_SettlesPricesAtItsTime_WithoutWriting_AndListsTheReadersOwn()
    {
        string item = Fresh(Dust);
        long seller = NewCharacter(100);
        long listing = List(seller, Give(seller, item, 10), item, 10).ListingId;
        long viewer = NewCharacter(100);
        string stored = Price(item);

        StoredMarket seen = Read(viewer, item, Now.AddDays(2.5));
        StoredMarket own = Read(seller, item, Now.AddDays(2.5));
        StoredMarket unlisted = Read(viewer, Fresh(Dust));

        Assert.That((seen.Entries.Single().Price, seen.Entries.Single().Stock), Is.EqualTo((24, 10L)));
        Assert.That(Price(item), Is.EqualTo(stored), "the read wrote nothing");
        Assert.That(seen.Listings, Is.Empty);
        Assert.That(
            own.Listings.Select(row => (row.Id, row.UnitsListed, row.UnitsLeft, row.GrossCoins, row.ExpiresAt)),
            Is.EqualTo(new[] { (listing, 10, 10, 0L, Now.AddDays(3)) }));
        Assert.That((unlisted.Entries.Single().Price, unlisted.Entries.Single().Stock), Is.EqualTo((28, 0L)),
            "an item never listed reads at its base");
    }
}
}
