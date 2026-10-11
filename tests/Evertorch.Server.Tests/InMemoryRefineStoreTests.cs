using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The in-memory store keeps PostgreSQL's refine rules (Persistence §5; <c>RefineStoreTests</c>): the outcome as
///     drawn, the coins and the ore taken, each emptied row reported at 0 under its own item, a refusal of each kind
///     changing nothing, and its switch losing a committed answer, which the lookup then finds.
/// </summary>
[TestFixture]
public sealed class InMemoryRefineStoreTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Ore = "item.material.emberstone";
    private const long Coins = 500;
    private const long Cost = 100;

    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryGameStore m_store = new();

    private long NewCharacter()
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
        m_store.Edit(id, coins: Coins);
        return id;
    }

    // A sword at the given level, then ore, as the character's first two rows.
    private (long Sword, long Ore) Give(long character, int level, int ore)
    {
        m_store.GiveItems(character, Sword, 1, 1, 0);
        m_store.GiveItems(character, Ore, 1, ore, 0);
        StoredCharacter stored = m_store.Stored(character);
        long sword = stored.Items.Single(row => row.ItemDefinitionId == Sword).Id;
        m_store.Refine(character, sword, level);
        return (sword, stored.Items.Single(row => row.ItemDefinitionId == Ore).Id);
    }

    private InventoryResult Refine(
        long character,
        (long Sword, long Ore) rows,
        int levelBefore,
        RefineOutcomeKind outcome,
        int levelAfter,
        Guid? operation = null,
        long cost = Cost)
    {
        return m_store.CommitRefineAsync(
                new RefineCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    rows.Sword,
                    Sword,
                    levelBefore,
                    outcome,
                    levelAfter,
                    rows.Ore,
                    Ore,
                    1,
                    cost,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static (long, string, int, int)[] Rows(InventoryResult result)
    {
        return result.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity, row.RefineLevel)).ToArray();
    }

    [Test]
    public void ARefine_OfARowAtAnotherLevel_Worn_ShortOfOre_OrOfCoins_IsRefusedByKind_AndChangesNothing()
    {
        long character = NewCharacter();
        (long Sword, long Ore) rows = Give(character, 3, 1);
        long worn = NewCharacter();
        (long Sword, long Ore) wornRows = Give(worn, 3, 1);
        m_store.Wear(worn, wornRows.Sword, "Weapon");

        InventoryStatus stale = Refine(character, rows, 2, RefineOutcomeKind.Refined, 3).Status;
        InventoryStatus wornStatus = Refine(worn, wornRows, 3, RefineOutcomeKind.Refined, 4).Status;
        InventoryStatus poor = Refine(character, rows, 3, RefineOutcomeKind.Refined, 4, cost: Coins + 1).Status;
        InventoryStatus noOre = m_store.CommitRefineAsync(
                new RefineCommit(Guid.NewGuid(), character, rows.Sword, Sword, 3, RefineOutcomeKind.Refined, 4,
                    rows.Ore, Ore, 2, Cost, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Status;

        Assert.That(
            (stale, wornStatus, poor, noOre),
            Is.EqualTo((InventoryStatus.Refused, InventoryStatus.RowWorn, InventoryStatus.NotEnoughCoins,
                InventoryStatus.MaterialShort)));
        StoredCharacter stored = m_store.Stored(character);
        Assert.That(
            stored.Items.Select(row => (row.Id, row.Quantity, row.RefineLevel)),
            Is.EqualTo(new[] { (rows.Sword, 1, 3), (rows.Ore, 1, 0) }));
        Assert.That(stored.Coins, Is.EqualTo(Coins));
        Assert.That(m_store.RefineAttempts.Keys.Intersect(
                m_store.RefineCommits.Where(commit => commit.CharacterId == character)
                    .Select(commit => commit.OperationId)),
            Is.Empty);
    }

    [Test]
    public void ARefine_ThatDestroys_WhoseAnswerIsLost_IsFound_WithBothRowsAtZeroUnderTheirItems()
    {
        long character = NewCharacter();
        (long Sword, long Ore) rows = Give(character, 7, 1);
        var operation = Guid.NewGuid();
        m_store.AmbiguousRefineFailures = 1;
        Action lost = () => Refine(character, rows, 7, RefineOutcomeKind.Destroyed, 0, operation);

        Assert.That(lost, Throws.InstanceOf<StoreUnavailableException>());
        InventoryResult found = m_store.FindOperationAsync(
                operation,
                character,
                new[] { new ReportedRow(rows.Ore, Ore) },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()!;

        Assert.That(found.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Rows(found), Is.EqualTo(new[] { (rows.Sword, Sword, 0, 0), (rows.Ore, Ore, 0, 0) }));
        Assert.That(found.Coins, Is.EqualTo(Coins - Cost));
        Assert.That(m_store.RefineAttempts[operation],
            Is.EqualTo((RefineOutcomeKind.Destroyed, 7, (int?)null, 1, Cost)));
    }

    [Test]
    public void ARefine_ThatRaises_Lowers_OrLeavesTheItem_AppliesTheOutcomeAsDrawn()
    {
        long character = NewCharacter();
        (long Sword, long Ore) rows = Give(character, 7, 5);
        var operation = Guid.NewGuid();
        int attempts = m_store.RefineAttempts.Count;

        InventoryResult raised = Refine(character, rows, 7, RefineOutcomeKind.Refined, 8, operation);
        InventoryResult again = Refine(character, rows, 7, RefineOutcomeKind.Refined, 8, operation);
        InventoryResult lowered = Refine(character, rows, 8, RefineOutcomeKind.Lowered, 6);
        InventoryResult kept = Refine(character, rows, 6, RefineOutcomeKind.Unchanged, 6);

        Assert.That(Rows(raised), Is.EqualTo(new[] { (rows.Sword, Sword, 1, 8), (rows.Ore, Ore, 4, 0) }));
        Assert.That(Rows(again), Is.EqualTo(Rows(raised)), "a repeat answers from the ledger");
        Assert.That(Rows(lowered), Is.EqualTo(new[] { (rows.Sword, Sword, 1, 6), (rows.Ore, Ore, 3, 0) }));
        Assert.That(Rows(kept), Is.EqualTo(new[] { (rows.Sword, Sword, 1, 6), (rows.Ore, Ore, 2, 0) }));
        Assert.That(kept.Coins, Is.EqualTo(Coins - 3 * Cost));
        Assert.That(m_store.RefineCommits.Count(commit => commit.CharacterId == character), Is.EqualTo(4));
        Assert.That(m_store.RefineAttempts.Count - attempts, Is.EqualTo(3), "the repeat recorded nothing");
    }
}
}
