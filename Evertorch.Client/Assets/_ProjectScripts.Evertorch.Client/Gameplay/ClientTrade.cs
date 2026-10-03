using System;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The player's trade as the server last told it (Gameplay Systems §16): a request waiting for an answer, and the
///     open trade with both sides as they stand. The server decides everything; this only shows it.
/// </summary>
public sealed class ClientTrade
{
    /// <summary>
    ///     How long a request waits for an answer, as the server keeps it.
    /// </summary>
    public const double RequestSeconds = 30.0;

    private static readonly TradeSide EmptyOwn =
        new(TradeSideOwner.Own, false, false, 0, Array.Empty<TradeEntry>());

    private static readonly TradeSide EmptyTheirs =
        new(TradeSideOwner.Partner, false, false, 0, Array.Empty<TradeEntry>());

    /// <summary>
    ///     The character asking the player to trade; null when no request waits.
    /// </summary>
    public string? Requester { get; private set; }

    /// <summary>
    ///     When, in the clock passed to <see cref="Stamp" />, the request waiting runs out.
    /// </summary>
    public double RequestEndsAt { get; private set; }

    /// <summary>
    ///     The partner of the open trade; null while none is open.
    /// </summary>
    public string? Partner { get; private set; }

    public bool IsOpen => Partner != null;

    /// <summary>
    ///     The player's own side, its rows named by their IDs.
    /// </summary>
    public TradeSide Own { get; private set; } = EmptyOwn;

    /// <summary>
    ///     The partner's side, its rows without their IDs.
    /// </summary>
    public TradeSide Theirs { get; private set; } = EmptyTheirs;

    public void Apply(TradeEvent message)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        switch (message.Kind)
        {
            case TradeEventKind.Requested:
                Requester = message.Name;
                RequestEndsAt = double.MaxValue;
                break;
            case TradeEventKind.Opened:
                Requester = null;
                Partner = message.Name;
                Own = EmptyOwn;
                Theirs = EmptyTheirs;
                break;
            case TradeEventKind.Cancelled:
            case TradeEventKind.Completed:
            case TradeEventKind.Failed:
                Partner = null;
                Own = EmptyOwn;
                Theirs = EmptyTheirs;
                break;
        }
    }

    public void Apply(TradeSide side)
    {
        if (side == null)
        {
            throw new ArgumentNullException(nameof(side));
        }

        if (!IsOpen)
        {
            return;
        }

        if (side.Owner == TradeSideOwner.Own)
        {
            Own = side;
        }
        else
        {
            Theirs = side;
        }
    }

    /// <summary>
    ///     Starts the request's 30 s from <paramref name="now" />, the moment the player heard of it.
    /// </summary>
    public void Stamp(double now)
    {
        if (Requester != null && RequestEndsAt == double.MaxValue)
        {
            RequestEndsAt = now + RequestSeconds;
        }
    }

    public void EndRequest()
    {
        Requester = null;
    }

    public void ExpireRequest(double now)
    {
        if (Requester != null && now >= RequestEndsAt)
        {
            EndRequest();
        }
    }

    /// <summary>
    ///     How much of the player's row <paramref name="inventoryItem" /> its side offers; 0 when none.
    /// </summary>
    public uint OfferedOf(long inventoryItem)
    {
        foreach (TradeEntry entry in Own.Entries)
        {
            if (entry.InventoryItem == inventoryItem)
            {
                return entry.Quantity;
            }
        }

        return 0;
    }
}
}
