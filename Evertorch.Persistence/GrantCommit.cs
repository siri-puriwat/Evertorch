using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One item the server grants a character, a boss's prize for its most valuable player (Persistence §5): a quantity
///     of one item under a new operation ID, for no coins. The item merges into its row unless its stack limit is 1, so
///     a grant adds at most one row.
/// </summary>
public sealed class GrantCommit
{
    public GrantCommit(
        Guid operationId,
        long characterId,
        string itemDefinitionId,
        int quantity,
        int stackLimit,
        int maxRows,
        DateTime at)
    {
        if (quantity < 1 || stackLimit < 1 || maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity, stack limit, and rows are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Quantity = quantity;
        StackLimit = stackLimit;
        MaxRows = maxRows;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    public int StackLimit { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
