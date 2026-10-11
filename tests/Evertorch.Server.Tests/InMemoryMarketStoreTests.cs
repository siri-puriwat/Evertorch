using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The in-memory store keeps PostgreSQL's market rules (Persistence §5; <c>MarketStoreTests</c>): a listing's fee
///     at the settled price, the cap, a collection's net less the tax and its units back, a refusal changing nothing,
///     and its switch losing a committed answer, which the ledger's lookup then finds.
/// </summary>
[TestFixture]
public sealed class InMemoryMarketStoreTests
{
    // A store of its own for each test, so no test meets another's prices.
    [SetUp]
    public void StartStore()
    {
        m_store = new InMemoryGameStore();
    }

    private const string Dust = "item.material.gloom_dust";
    private const long Period = 86_400_000;

    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemMarket DustTerms = new(28, 14, 280);

    private InMemoryGameStore m_store = null!;

    private long NewCharacter(long coins)
    {
        AccountId account = m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
        var character = new NewCharacter(
            $"Mem{Guid.NewGuid():N}".Substring(0, 12),
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 2f),
            Now);
        long id = m_store.CreateCharacterAsync(account, character, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .CharacterId;
        m_store.Edit(id, coins: coins);
        return id;
    }

    private long GiveDust(long character, int quantity)
    {
        m_store.GiveItems(character, Dust, 1, quantity, 0);
        return m_store.Stored(character).Items.Single(item => item.ItemDefinitionId == Dust).Id;
    }

    private MarketResult List(long character, long row, int quantity, Guid? operation = null, DateTime? at = null)
    {
        return m_store.CommitMarketListAsync(
                new MarketListCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    row,
                    Dust,
                    quantity,
                    DustTerms,
                    Period,
                    3 * Period,
                    MarketTerms.MaxListings,
                    at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private MarketResult Collect(long character, long listing)
    {
        return m_store.CommitMarketCollectAsync(
                new MarketCollectCommit(
                    Guid.NewGuid(),
                    character,
                    listing,
                    new Dictionary<string, int> { [Dust] = 999 },
                    new Dictionary<string, ItemMarket> { [Dust] = DustTerms },
                    100,
                    Period,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private MarketResult Buy(long character, int quantity, int priceLimit, DateTime? at = null)
    {
        return m_store.CommitMarketBuyAsync(
                new MarketBuyCommit(
                    Guid.NewGuid(),
                    character,
                    Dust,
                    quantity,
                    priceLimit,
                    999,
                    100,
                    DustTerms,
                    Period,
                    at ?? Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private StoredMarket Read(long character, DateTime at)
    {
        return m_store.ReadMarketAsync(
                new MarketRead(character, new Dictionary<string, ItemMarket> { [Dust] = DustTerms }, Period, at),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Test]
    public void AListing_WhoseAnswerIsLost_IsFound_AndItsCollectionPaysTheNet()
    {
        long seller = NewCharacter(100);
        long dust = GiveDust(seller, 10);
        var operation = Guid.NewGuid();
        m_store.AmbiguousMarketFailures = 1;

        Action lost = () => List(seller, dust, 10, operation);

        Assert.That(lost, Throws.InstanceOf<StoreUnavailableException>());
        InventoryResult? found = m_store.FindOperationAsync(operation, seller, Array.Empty<ReportedRow>(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((found!.Status, found.Coins, found.Rows.Single().Quantity),
            Is.EqualTo((InventoryStatus.Committed, 98L, 0)));
        MarketResult repeat = List(seller, dust, 10, operation);
        m_store.SellFromListing(repeat.ListingId, 4, 120);
        MarketResult collected = Collect(seller, repeat.ListingId);
        Assert.That(
            (collected.Status, collected.Coins, collected.CoinsMoved, collected.Quantity, collected.Tax),
            Is.EqualTo((InventoryStatus.Committed, 208L, 110L, 6, 10L)));
        Assert.That(Collect(seller, repeat.ListingId).Status, Is.EqualTo(InventoryStatus.BoardChanged), "gone");
    }

    [Test]
    public void APurchase_TakesTheOldestUnitsOfOtherAccounts_AndTheLastOneRaisesThePrice()
    {
        long first = NewCharacter(100);
        long second = NewCharacter(100);
        long buyer = NewCharacter(1000);
        long older = List(first, GiveDust(first, 3), 3).ListingId;
        long newer = List(second, GiveDust(second, 3), 3, at: Now.AddHours(1)).ListingId;

        MarketResult bought = Buy(buyer, 6, 28, Now.AddHours(2));
        StoredMarket after = Read(buyer, Now.AddHours(2));

        Assert.That(
            bought.Fills.Select(fill => (fill.ListingId, fill.Units, fill.Gross)),
            Is.EqualTo(new[] { (older, 3, 84L), (newer, 3, 84L) }));
        Assert.That((bought.Coins, bought.Rows.Single().Quantity), Is.EqualTo((832L, 6)));
        Assert.That((after.Entries.Single().Price, after.Entries.Single().Stock), Is.EqualTo((30, 0L)),
            "the buyer took the last units it could");
        Assert.That(Buy(buyer, 1, 30, Now.AddHours(3)).Status, Is.EqualTo(InventoryStatus.BoardChanged), "none left");
    }

    [Test]
    public void Refusals_ChangeNothing_AndTheFeeFollowsTheSettledPrice()
    {
        long poor = NewCharacter(0);
        long busy = NewCharacter(100);
        for (int index = 0; index < MarketTerms.MaxListings; index++)
        {
            List(busy, GiveDust(busy, 1), 1);
        }

        Assert.That(List(poor, GiveDust(poor, 5), 5).Status, Is.EqualTo(InventoryStatus.NotEnoughCoins));
        Assert.That(List(busy, GiveDust(busy, 1), 1).Status, Is.EqualTo(InventoryStatus.ListingCapReached));
        Assert.That(m_store.Stored(poor).InventoryRevision, Is.Zero);
        long seller = NewCharacter(100);
        MarketResult later = List(seller, GiveDust(seller, 100), 100, at: Now.AddDays(2.5));
        Assert.That(later.CoinsMoved, Is.EqualTo(24L), "the busy seller's stock made the price fall twice");
    }

    [Test]
    public void TheRead_SettlesPricesWithoutWriting_AndKeepsItsOwnAccountsStockOut()
    {
        long seller = NewCharacter(100);
        long listing = List(seller, GiveDust(seller, 10), 10).ListingId;
        long buyer = NewCharacter(100);

        StoredMarket later = Read(buyer, Now.AddDays(2.5));
        StoredMarket own = Read(seller, Now);

        Assert.That((later.Entries.Single().Price, later.Entries.Single().Stock), Is.EqualTo((24, 10L)));
        Assert.That((own.Entries.Single().Price, own.Entries.Single().Stock), Is.EqualTo((28, 0L)),
            "the earlier read wrote nothing, and its own stock is not for it");
        Assert.That(own.Listings.Single().Id, Is.EqualTo(listing));
    }
}
}
