using System;

namespace Evertorch.Game
{
/// <summary>
///     The board's price of one item as stored (Gameplay Systems §11.5): the price, the listed time not yet turned into
///     falls, when the price was last settled, the latest expiry of the item's listings with units left, and its last
///     rise. Times are UTC.
/// </summary>
public sealed class MarketPriceState
{
    public MarketPriceState(int price, long listedMs, DateTime settledAt, DateTime? stockUntil, DateTime? roseAt)
    {
        if (price < 1 || listedMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "A price is at least 1 coin, and the listed time is not negative.");
        }

        Price = price;
        ListedMs = listedMs;
        SettledAt = settledAt;
        StockUntil = stockUntil;
        RoseAt = roseAt;
    }

    public int Price { get; }

    /// <summary>
    ///     Time the item had stock listed since its last fall or rise, less than a period once settled.
    /// </summary>
    public long ListedMs { get; }

    public DateTime SettledAt { get; }

    /// <summary>
    ///     The latest expiry of the item's listings with units left; null while none has any.
    /// </summary>
    public DateTime? StockUntil { get; }

    public DateTime? RoseAt { get; }

    /// <summary>
    ///     A new item's state: its base price, settled when it is first listed.
    /// </summary>
    public static MarketPriceState Start(int basePrice, DateTime at)
    {
        return new MarketPriceState(basePrice, 0, at, null, null);
    }

    public MarketPriceState WithStockUntil(DateTime? stockUntil)
    {
        return new MarketPriceState(Price, ListedMs, SettledAt, stockUntil, RoseAt);
    }
}
}
