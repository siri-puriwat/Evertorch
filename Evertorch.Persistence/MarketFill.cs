namespace Evertorch.Persistence
{
/// <summary>
///     One listing a purchase took units from (Persistence §5): its seller, the units, and their gross coins, which wait
///     on the listing until the seller collects them.
/// </summary>
public sealed class MarketFill
{
    public MarketFill(long listingId, long sellerCharacterId, int units, long gross)
    {
        ListingId = listingId;
        SellerCharacterId = sellerCharacterId;
        Units = units;
        Gross = gross;
    }

    public long ListingId { get; }

    public long SellerCharacterId { get; }

    public int Units { get; }

    public long Gross { get; }
}
}
