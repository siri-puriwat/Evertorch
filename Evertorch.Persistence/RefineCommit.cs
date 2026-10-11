using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     One refine to commit (Persistence §5): the row to refine at the level the tick thread saw, the outcome its one draw
///     decided, and what it costs, the ore's row and quantity and the coins, under a new operation ID. The store checks
///     the row, the ore, and the coins again and applies the outcome as it is; it never draws.
/// </summary>
public sealed class RefineCommit
{
    public RefineCommit(
        Guid operationId,
        long characterId,
        long inventoryItemId,
        string itemDefinitionId,
        int levelBefore,
        RefineOutcomeKind outcome,
        int levelAfter,
        long materialItemId,
        string materialDefinitionId,
        int materialQuantity,
        long coins,
        DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        LevelBefore = levelBefore;
        Outcome = outcome;
        LevelAfter = levelAfter;
        MaterialItemId = materialItemId;
        MaterialDefinitionId = materialDefinitionId ?? throw new ArgumentNullException(nameof(materialDefinitionId));
        MaterialQuantity = materialQuantity;
        Coins = coins;
        At = at;
        ReportRows = new[] { new ReportedRow(materialItemId, MaterialDefinitionId) };
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    /// <summary>The row refined, which the ledger row names.</summary>
    public long InventoryItemId { get; }

    public string ItemDefinitionId { get; }

    /// <summary>The level the row must still be at.</summary>
    public int LevelBefore { get; }

    public RefineOutcomeKind Outcome { get; }

    /// <summary>The level a raise or a fall leaves; the level before when unchanged, and unused when destroyed.</summary>
    public int LevelAfter { get; }

    /// <summary>The ore's row, deleted once emptied.</summary>
    public long MaterialItemId { get; }

    public string MaterialDefinitionId { get; }

    public int MaterialQuantity { get; }

    public long Coins { get; }

    public DateTime At { get; }

    /// <summary>
    ///     The ore's row, which the answer reports beside the refined one under its own item (Persistence §5).
    /// </summary>
    public IReadOnlyCollection<ReportedRow> ReportRows { get; }
}
}
