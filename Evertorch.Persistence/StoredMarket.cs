using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     The board as one character reads it (Persistence §5): each named item's entry, in item order, and the
///     character's own listings, in listing order, all of one snapshot.
/// </summary>
public sealed class StoredMarket
{
    public StoredMarket(IReadOnlyList<StoredMarketEntry> entries, IReadOnlyList<StoredMarketListing> listings)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Listings = listings ?? throw new ArgumentNullException(nameof(listings));
    }

    public IReadOnlyList<StoredMarketEntry> Entries { get; }

    public IReadOnlyList<StoredMarketListing> Listings { get; }
}
}
