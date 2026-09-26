using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One sale to commit (Persistence §5): a quantity of one row at the item's sell price, under a new operation ID.
/// </summary>
public sealed class SellCommit
{
    public SellCommit(Guid operationId, long characterId, long inventoryItemId, int quantity, int price, DateTime at)
    {
        if (quantity < 1 || price < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity and price are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        Quantity = quantity;
        Price = price;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long InventoryItemId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     What one unit fetches.
    /// </summary>
    public int Price { get; }

    /// <summary>
    ///     What the sale fetches, in 64-bit arithmetic.
    /// </summary>
    public long Proceeds => (long)Price * Quantity;

    public DateTime At { get; }
}
}
