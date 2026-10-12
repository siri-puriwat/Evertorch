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
///     The in-memory store keeps PostgreSQL's rules for a row's cards (Persistence §4, §5; <c>CardStoreTests</c>): a
///     row keeps its cards wherever it goes, and a second item with a stack limit of 1 takes a row of its own.
/// </summary>
[TestFixture]
public sealed class InMemoryCardStoreTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";

    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryGameStore m_store = new();

    private long NewCharacter(AccountId account)
    {
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
        m_store.Edit(id, coins: 100);
        return id;
    }

    private AccountId NewAccount()
    {
        return m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
    }

    // A sword with Keen and Bane at +7, the character's only row.
    private long CardedSword(long character)
    {
        m_store.GiveItems(character, Sword, 1, 1, 0);
        long sword = m_store.Stored(character).Items.Single().Id;
        m_store.Refine(character, sword, 7);
        m_store.Card(character, sword, Keen, Bane);
        return sword;
    }

    [Test]
    public void ACardedRow_KeepsItsCards_ThroughADepositAndAWithdrawal()
    {
        AccountId account = NewAccount();
        long depositor = NewCharacter(account);
        long other = NewCharacter(account);
        long sword = CardedSword(depositor);

        StorageResult stored = m_store.CommitStorageDepositAsync(
                new StorageDepositCommit(Guid.NewGuid(), depositor, sword, 1, 20, 1, 300, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        StoredStorage read = m_store.ReadStorageAsync(account, CancellationToken.None).GetAwaiter().GetResult();
        StorageResult back = m_store.CommitStorageWithdrawAsync(
                new StorageWithdrawCommit(
                    Guid.NewGuid(),
                    other,
                    stored.StorageRow!.Id,
                    1,
                    new Dictionary<string, int> { [Sword] = 1 },
                    100,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(stored.StorageRow.Cards, Is.EqualTo(new[] { Keen, Bane }), "the deposit's answer");
        Assert.That(read.Items.Single().Cards, Is.EqualTo(new[] { Keen, Bane }), "the read");
        Assert.That((back.Row!.RefineLevel, back.Row.Cards), Is.EqualTo((7, new[] { Keen, Bane })), "the withdrawal");
        Assert.That(m_store.Stored(other).Items.Single().Cards, Is.EqualTo(new[] { Keen, Bane }), "the load");
    }

    [Test]
    public void Pickup_OfASecondItemWithAStackLimitOfOne_TakesARowOfItsOwn()
    {
        long character = NewCharacter(NewAccount());
        m_store.GiveItems(character, Sword, 1, 1, 0);

        InventoryResult result = m_store.CommitPickupAsync(
                new PickupCommit(Guid.NewGuid(), character, Sword, 1, 1, 100, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(m_store.Stored(character).Items.Select(item => (item.ItemDefinitionId, item.Quantity)),
            Is.EqualTo(new[] { (Sword, 1), (Sword, 1) }));
    }
}
}
