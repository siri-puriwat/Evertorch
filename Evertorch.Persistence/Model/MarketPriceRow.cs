using System;

namespace Evertorch.Persistence
{
/// <summary>
///     The board's price state of one listable item (Persistence §4), changed only inside a listing's, a purchase's, or a
///     collection's commit.
/// </summary>
internal sealed class MarketPriceRow
{
    public string ItemDefinitionId { get; set; } = string.Empty;

    public int Price { get; set; }

    public long ListedMs { get; set; }

    public DateTime SettledAt { get; set; }

    public DateTime? StockUntil { get; set; }

    public DateTime? RoseAt { get; set; }

    public int Version { get; set; }
}
}
