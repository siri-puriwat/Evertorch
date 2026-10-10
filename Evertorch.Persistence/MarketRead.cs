using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     One read of the board for one character (Persistence §5): the prices of the named items at the read's time and
///     the units the character may buy of each, then the character's own listings.
/// </summary>
public sealed class MarketRead
{
    public MarketRead(
        long characterId,
        IReadOnlyDictionary<string, ItemMarket> items,
        long pricePeriodMs,
        DateTime at)
    {
        if (pricePeriodMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pricePeriodMs), "A period is at least 1 ms.");
        }

        CharacterId = characterId;
        Items = items ?? throw new ArgumentNullException(nameof(items));
        PricePeriodMs = pricePeriodMs;
        At = at;
    }

    public long CharacterId { get; }

    /// <summary>
    ///     The items whose entries the read answers, by item ID, with their terms: every listable item for an open, the
    ///     one a command touched after it settles.
    /// </summary>
    public IReadOnlyDictionary<string, ItemMarket> Items { get; }

    public long PricePeriodMs { get; }

    public DateTime At { get; }
}
}
