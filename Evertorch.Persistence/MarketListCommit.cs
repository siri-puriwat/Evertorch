using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     One listing to commit (Persistence §5): a quantity of one of the character's bag rows put on the board, for a fee
///     at the item's price of the moment, under a new operation ID. The item's terms and the board's periods come from
///     the content and the server's options.
/// </summary>
public sealed class MarketListCommit
{
    public MarketListCommit(
        Guid operationId,
        long characterId,
        long inventoryItemId,
        string itemDefinitionId,
        int quantity,
        ItemMarket terms,
        long pricePeriodMs,
        long listingDurationMs,
        int maxListings,
        DateTime at)
    {
        if (quantity < 1 || pricePeriodMs < 1 || listingDurationMs < 1 || maxListings < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity, periods, and listings are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Quantity = quantity;
        Terms = terms ?? throw new ArgumentNullException(nameof(terms));
        PricePeriodMs = pricePeriodMs;
        ListingDurationMs = listingDurationMs;
        MaxListings = maxListings;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long InventoryItemId { get; }

    /// <summary>
    ///     The item the server's copy of the row holds; a row holding anything else is refused.
    /// </summary>
    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    public ItemMarket Terms { get; }

    public long PricePeriodMs { get; }

    public long ListingDurationMs { get; }

    public int MaxListings { get; }

    public DateTime At { get; }
}
}
