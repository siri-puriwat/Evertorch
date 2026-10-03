using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One face-to-face trade to commit (Persistence §5): what each of its two traders gives the other, under the
///     trade's ID. The first trader has the lower character ID, so both are locked in the order a party's creation locks
///     them.
/// </summary>
public sealed class TradeCommit
{
    public TradeCommit(
        Guid tradeId,
        TraderOffer first,
        TraderOffer second,
        IReadOnlyDictionary<string, int> stackLimits,
        int maxRows,
        DateTime at)
    {
        First = first ?? throw new ArgumentNullException(nameof(first));
        Second = second ?? throw new ArgumentNullException(nameof(second));
        StackLimits = stackLimits ?? throw new ArgumentNullException(nameof(stackLimits));
        if (first.CharacterId >= second.CharacterId)
        {
            throw new ArgumentException("The first trader has the lower character ID.", nameof(first));
        }

        if (maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), "A bag holds at least one row.");
        }

        TradeId = tradeId;
        MaxRows = maxRows;
        At = at;
    }

    /// <summary>
    ///     The trade's ID: the key of its <c>trades</c> row, and the namespace every ledger row's operation ID is derived
    ///     from.
    /// </summary>
    public Guid TradeId { get; }

    public TraderOffer First { get; }

    public TraderOffer Second { get; }

    /// <summary>
    ///     The stack limit of every item either side offers, by item definition ID.
    /// </summary>
    public IReadOnlyDictionary<string, int> StackLimits { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
