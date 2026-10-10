using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One listing on the board (Persistence §4): its seller's unsold units and the gross coins of its sales, which wait
///     on it until the seller collects them.
/// </summary>
internal sealed class MarketListingRow
{
    public long Id { get; set; }

    public long SellerCharacterId { get; set; }

    public long SellerAccountId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int UnitsListed { get; set; }

    public int UnitsLeft { get; set; }

    public long GrossCoins { get; set; }

    public DateTime ListedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int Version { get; set; }
}
}
