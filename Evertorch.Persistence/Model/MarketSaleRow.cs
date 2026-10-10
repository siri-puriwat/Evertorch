using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One listing a purchase filled (Persistence §4), append-only like the ledger: the purchase's operation, the
///     listing, its seller, the buyer, the item, the units, and their gross coins. The listing is kept as a number, since
///     a collected listing is deleted.
/// </summary>
internal sealed class MarketSaleRow
{
    public Guid OperationId { get; set; }

    public long ListingId { get; set; }

    public long SellerCharacterId { get; set; }

    public long BuyerCharacterId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int Units { get; set; }

    public long GrossCoins { get; set; }
}
}
