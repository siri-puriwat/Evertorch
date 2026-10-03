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
///     The in-memory store keeps PostgreSQL's trade rules through the same settlement (Persistence §5;
///     <c>TradeStoreTests</c>), so the server's tests see the same outcomes, and its switch loses a committed trade's
///     answer, which the lookup then finds.
/// </summary>
[TestFixture]
public sealed class InMemoryTradeStoreTests
{
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.training_sword";

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, int> Limits =
        new Dictionary<string, int> { [Gel] = 999, [Sword] = 1 };

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

    private long Row(long character, string item)
    {
        return m_store.Stored(character).Items.Last(row => row.ItemDefinitionId == item).Id;
    }

    private TradeResult Trade(Guid trade, long first, TradeLine[] firstLines, long firstCoins, long second,
        long secondCoins)
    {
        return m_store.CommitTradeAsync(
                new TradeCommit(
                    trade,
                    new TraderOffer(first, firstLines, firstCoins),
                    new TraderOffer(second, Array.Empty<TradeLine>(), secondCoins),
                    Limits,
                    100,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Test]
    public void AmbiguousTradeFailures_CommitTheTrade_ThenLoseItsAnswer_WhichTheLookupFinds()
    {
        long ann = NewCharacter(0);
        long bob = NewCharacter(30);
        m_store.GiveItems(ann, Gel, 1, 10, 0);
        m_store.Edit(ann, item: Sword);
        long gel = Row(ann, Gel);
        long sword = Row(ann, Sword);
        var trade = Guid.NewGuid();
        m_store.AmbiguousTradeFailures = 1;

        Action lost = () => Trade(trade, ann, new[] { new TradeLine(gel, 4), new TradeLine(sword, 1) }, 0, bob, 30);

        Assert.That(lost, Throws.InstanceOf<StoreUnavailableException>());
        TradeResult? found = m_store.FindTradeAsync(trade, ann, bob, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(found!.Status, Is.EqualTo(TradeStatus.Committed));
        Assert.That(
            m_store.Stored(bob).Items.Select(row => (row.ItemDefinitionId, row.Quantity)),
            Is.EquivalentTo(new[] { (Gel, 4), (Sword, 1) }));
        Assert.That(Row(bob, Sword), Is.EqualTo(sword), "the whole row kept its ID");
        Assert.That((m_store.Stored(ann).Coins, m_store.Stored(bob).Coins), Is.EqualTo((30L, 0L)));
        Assert.That(
            (m_store.Stored(ann).InventoryRevision, m_store.Stored(bob).InventoryRevision),
            Is.EqualTo((1u, 1u)));
        Assert.That(Trade(trade, ann, new[] { new TradeLine(gel, 4) }, 0, bob, 30).Status,
            Is.EqualTo(TradeStatus.Committed), "a repeat changes nothing");
        Assert.That(m_store.Stored(ann).Items.Single(row => row.Id == gel).Quantity, Is.EqualTo(6));
    }

    [Test]
    public void Refusals_NameTheirSide_AndMoveNothing()
    {
        long ann = NewCharacter(10);
        long bob = NewCharacter(ContentLimits.MaxCurrency);
        int asked = m_store.TradeCommits.Count;

        TradeResult shortCoins = Trade(Guid.NewGuid(), ann, Array.Empty<TradeLine>(), 11, bob, 0);
        TradeResult pastTheCap = Trade(Guid.NewGuid(), ann, Array.Empty<TradeLine>(), 1, bob, 0);

        Assert.That(
            new[]
            {
                (shortCoins.Status, shortCoins.RefusedCharacterId), (pastTheCap.Status, pastTheCap.RefusedCharacterId)
            },
            Is.EqualTo(new[] { (TradeStatus.Refused, (long?)ann), (TradeStatus.CoinCapReached, (long?)bob) }));
        Assert.That(m_store.Stored(ann).InventoryRevision, Is.Zero);
        Assert.That(m_store.TradeCommits, Has.Count.EqualTo(asked + 2));
    }
}
}
