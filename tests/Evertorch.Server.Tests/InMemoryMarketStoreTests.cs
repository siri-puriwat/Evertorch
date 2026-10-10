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
    private const string Dust = "item.material.gloom_dust";
    private const long Period = 86_400_000;

    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemMarket DustTerms = new(28, 14, 280);

    private readonly InMemoryGameStore m_store = new();

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

    [Test]
    public void AListing_WhoseAnswerIsLost_IsFound_AndItsCollectionPaysTheNet()
    {
        long seller = NewCharacter(100);
        long dust = GiveDust(seller, 10);
        var operation = Guid.NewGuid();
        m_store.AmbiguousMarketFailures = 1;

        Action lost = () => List(seller, dust, 10, operation);

        Assert.That(lost, Throws.InstanceOf<StoreUnavailableException>());
        InventoryResult? found = m_store.FindOperationAsync(operation, seller, Array.Empty<long>(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((found!.Status, found.Coins, found.Rows.Single().Quantity),
            Is.EqualTo((InventoryStatus.Committed, 98L, 0)));
        MarketResult repeat = List(seller, dust, 10, operation);
        m_store.SellFromListing(repeat.ListingId, 4, 120);
        MarketResult collected = Collect(seller, repeat.ListingId);
        Assert.That(
            (collected.Status, collected.Coins, collected.CoinsMoved, collected.Quantity),
            Is.EqualTo((InventoryStatus.Committed, 208L, 110L, 6)));
        Assert.That(Collect(seller, repeat.ListingId).Status, Is.EqualTo(InventoryStatus.BoardChanged), "gone");
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
}
}
