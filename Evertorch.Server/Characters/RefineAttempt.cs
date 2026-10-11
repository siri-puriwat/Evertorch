using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     What one refine's draw decided, kept on its operation until it settles, a lost answer's too (Network Protocol
///     §9): the row and its item, its levels before and after, and the outcome.
/// </summary>
public sealed class RefineAttempt
{
    public RefineAttempt(
        long inventoryItem,
        ItemDefinitionId item,
        int levelBefore,
        int levelAfter,
        RefineOutcomeKind outcome)
    {
        InventoryItem = inventoryItem;
        Item = item;
        LevelBefore = levelBefore;
        LevelAfter = levelAfter;
        Outcome = outcome;
    }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    public int LevelBefore { get; }

    /// <summary>
    ///     0 for a destroyed row.
    /// </summary>
    public int LevelAfter { get; }

    public RefineOutcomeKind Outcome { get; }
}
}
