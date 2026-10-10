using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     One collection to commit (Persistence §5): the character's listing ends, and its sales less the tax and its
///     unsold units come back, under a new operation ID. The server keeps no view of the board, so the commit carries
///     the stack limit of every item and the terms of every listable one.
/// </summary>
public sealed class MarketCollectCommit
{
    public MarketCollectCommit(
        Guid operationId,
        long characterId,
        long listingId,
        IReadOnlyDictionary<string, int> stackLimits,
        IReadOnlyDictionary<string, ItemMarket> terms,
        int maxRows,
        long pricePeriodMs,
        DateTime at)
    {
        if (listingId < 1 || maxRows < 1 || pricePeriodMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(listingId), "A listing, rows, and a period are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        ListingId = listingId;
        StackLimits = stackLimits ?? throw new ArgumentNullException(nameof(stackLimits));
        Terms = terms ?? throw new ArgumentNullException(nameof(terms));
        MaxRows = maxRows;
        PricePeriodMs = pricePeriodMs;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long ListingId { get; }

    /// <summary>
    ///     Each item's stack limit, by item ID; a listing of an item missing here is one the content lost.
    /// </summary>
    public IReadOnlyDictionary<string, int> StackLimits { get; }

    /// <summary>
    ///     Each listable item's terms, by item ID, with which its price is settled.
    /// </summary>
    public IReadOnlyDictionary<string, ItemMarket> Terms { get; }

    public int MaxRows { get; }

    public long PricePeriodMs { get; }

    public DateTime At { get; }
}
}
