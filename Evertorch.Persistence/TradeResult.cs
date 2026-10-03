using System;

namespace Evertorch.Persistence
{
/// <summary>
///     How a trade's commit ended (Persistence §5), with both traders' whole inventories as they are now, committed or
///     not. A refusal names the side it found wanting; nothing moved.
/// </summary>
public sealed class TradeResult
{
    public TradeResult(TradeStatus status, long? refusedCharacterId, TraderInventory first, TraderInventory second)
    {
        Status = status;
        RefusedCharacterId = refusedCharacterId;
        First = first ?? throw new ArgumentNullException(nameof(first));
        Second = second ?? throw new ArgumentNullException(nameof(second));
    }

    public TradeStatus Status { get; }

    /// <summary>
    ///     The character a refusal names: the giver whose offer no longer holds, or the receiver whose bag or coins
    ///     cannot take it; null when committed.
    /// </summary>
    public long? RefusedCharacterId { get; }

    public TraderInventory First { get; }

    public TraderInventory Second { get; }
}
}
