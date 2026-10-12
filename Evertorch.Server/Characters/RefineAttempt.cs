using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     What one refine's draw decided, kept on its operation until it settles, a lost answer's too (Network Protocol
///     §9): the row and its item, its levels before and after, the outcome, and the row's cards.
/// </summary>
public sealed class RefineAttempt
{
    public RefineAttempt(
        long inventoryItem,
        ItemDefinitionId item,
        int levelBefore,
        int levelAfter,
        RefineOutcomeKind outcome,
        IReadOnlyList<ItemDefinitionId>? cards = null)
    {
        InventoryItem = inventoryItem;
        Item = item;
        LevelBefore = levelBefore;
        LevelAfter = levelAfter;
        Outcome = outcome;
        Cards = cards ?? Array.Empty<ItemDefinitionId>();
    }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    public int LevelBefore { get; }

    /// <summary>
    ///     0 for a destroyed row.
    /// </summary>
    public int LevelAfter { get; }

    public RefineOutcomeKind Outcome { get; }

    /// <summary>
    ///     The row's cards as the tick thread saw them, which come back to the bag when it is destroyed (Gameplay Systems
    ///     §11.6).
    /// </summary>
    public IReadOnlyList<ItemDefinitionId> Cards { get; }
}
}
