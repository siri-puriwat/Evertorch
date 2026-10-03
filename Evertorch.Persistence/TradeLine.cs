using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A quantity of one row of the giver's bag.
/// </summary>
public sealed class TradeLine
{
    public TradeLine(long inventoryItemId, int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A line moves at least one unit.");
        }

        InventoryItemId = inventoryItemId;
        Quantity = quantity;
    }

    public long InventoryItemId { get; }

    public int Quantity { get; }
}
}
