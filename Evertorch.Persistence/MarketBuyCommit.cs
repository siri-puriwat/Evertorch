using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     One purchase on the board to commit (Persistence §5): a quantity of an item, from the oldest units other accounts
///     listed, at the item's price of the moment and at most the buyer's limit a unit, under a new operation ID. The
///     units go into one bag row, so the commit carries the item's stack limit and the most rows a bag holds.
/// </summary>
public sealed class MarketBuyCommit
{
    public MarketBuyCommit(
        Guid operationId,
        long characterId,
        string itemDefinitionId,
        int quantity,
        int priceLimit,
        int stackLimit,
        int maxRows,
        ItemMarket terms,
        long pricePeriodMs,
        DateTime at)
    {
        if (quantity < 1 || priceLimit < 1 || stackLimit < 1 || maxRows < 1 || pricePeriodMs < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity, the price limit, the stack limit, rows, and the period are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Quantity = quantity;
        PriceLimit = priceLimit;
        StackLimit = stackLimit;
        MaxRows = maxRows;
        Terms = terms ?? throw new ArgumentNullException(nameof(terms));
        PricePeriodMs = pricePeriodMs;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The most the buyer pays a unit: a price that rose past it refuses the purchase.
    /// </summary>
    public int PriceLimit { get; }

    public int StackLimit { get; }

    public int MaxRows { get; }

    public ItemMarket Terms { get; }

    public long PricePeriodMs { get; }

    public DateTime At { get; }
}
}
