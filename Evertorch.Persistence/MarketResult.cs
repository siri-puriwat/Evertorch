using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     How a listing, a purchase, or a collection ended (Persistence §5): the character's inventory revision and coins
///     and, when committed, the bag's one row it changed, as it is now (a row it emptied at 0), with what moved: the
///     listing, its item, the units, and the coins (the fee, the price paid, or the net collected), for a purchase each
///     listing it filled, and for a collection its tax. A refused collection names the listing's item, so a content that
///     lost it can be told.
/// </summary>
public sealed class MarketResult
{
    public MarketResult(
        InventoryStatus status,
        uint inventoryRevision,
        long coins,
        IReadOnlyList<StoredItem> rows,
        long listingId = 0,
        string itemDefinitionId = "",
        int quantity = 0,
        long coinsMoved = 0,
        IReadOnlyList<MarketFill>? fills = null,
        long tax = 0)
    {
        Status = status;
        InventoryRevision = inventoryRevision;
        Coins = coins;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        ListingId = listingId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Quantity = quantity;
        CoinsMoved = coinsMoved;
        Fills = fills ?? Array.Empty<MarketFill>();
        Tax = tax;
    }

    public InventoryStatus Status { get; }

    public uint InventoryRevision { get; }

    public long Coins { get; }

    public IReadOnlyList<StoredItem> Rows { get; }

    /// <summary>
    ///     The listing made or collected; 0 for a purchase.
    /// </summary>
    public long ListingId { get; }

    public string ItemDefinitionId { get; }

    /// <summary>
    ///     The units listed, bought, or given back.
    /// </summary>
    public int Quantity { get; }

    /// <summary>
    ///     The fee of a listing, the price of a purchase, or the net of a collection.
    /// </summary>
    public long CoinsMoved { get; }

    public IReadOnlyList<MarketFill> Fills { get; }

    /// <summary>
    ///     The tax a collection paid on its sales; 0 for a listing or a purchase.
    /// </summary>
    public long Tax { get; }
}
}
