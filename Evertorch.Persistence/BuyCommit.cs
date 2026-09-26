using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One purchase to commit (Persistence §5): a quantity of one item at the NPC's price, under a new operation ID.
///     The item merges into its row unless its stack limit is 1, so a purchase adds at most one row.
/// </summary>
public sealed class BuyCommit
{
    public BuyCommit(
        Guid operationId,
        long characterId,
        string itemDefinitionId,
        int quantity,
        int price,
        int stackLimit,
        int maxRows,
        DateTime at)
    {
        if (quantity < 1 || price < 1 || stackLimit < 1 || maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity, price, stack limit, and rows are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Quantity = quantity;
        Price = price;
        StackLimit = stackLimit;
        MaxRows = maxRows;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The price of one unit.
    /// </summary>
    public int Price { get; }

    /// <summary>
    ///     What the purchase costs, in 64-bit arithmetic.
    /// </summary>
    public long Total => (long)Price * Quantity;

    public int StackLimit { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
