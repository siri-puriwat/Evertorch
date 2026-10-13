using System;
using System.Collections.Generic;
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

    // A side's rows as its parts arrive, held until the last (Network Protocol §6).
    private readonly SideParts m_ownParts = new();
    private readonly SideParts m_theirParts = new();

    private string? m_answered;
    private double m_answeredEndsAt;

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
                m_ownParts.Clear();
                m_theirParts.Clear();
                break;
            case TradeEventKind.Cancelled:
            case TradeEventKind.Completed:
            case TradeEventKind.Failed:
                Partner = null;
                Own = EmptyOwn;
                Theirs = EmptyTheirs;
                m_ownParts.Clear();
                m_theirParts.Clear();
                break;
        }
    }

    /// <summary>
    ///     Takes one part of a side; the last part shows the whole side. False for a part out of its side's order, of
    ///     another count than its first, or past a side's rows, which only a faulty server sends: the side's parts so far
    ///     are dropped and the side shown stays as it was.
    /// </summary>
    public bool Apply(TradeSide side)
    {
        if (side == null)
        {
            throw new ArgumentNullException(nameof(side));
        }

        if (!IsOpen)
        {
            return true;
        }

        SideParts parts = side.Owner == TradeSideOwner.Own ? m_ownParts : m_theirParts;
        if (side.Part == 0)
        {
            parts.Clear();
            parts.Count = side.PartCount;
        }

        if (side.Part != parts.Next
            || side.PartCount != parts.Count
            || parts.Rows.Count + side.Entries.Count > TradeSide.MaxEntries)
        {
            parts.Clear();
            return false;
        }

        parts.Rows.AddRange(side.Entries);
        parts.Next++;
        if (!side.IsLast)
        {
            return true;
        }

        // Every part of a side carries its flags and coins; the last completes its rows.
        TradeSide whole = side.PartCount == 1
            ? side
            : new TradeSide(side.Owner, side.IsLocked, side.IsConfirmed, side.Coins, parts.Rows.ToArray());
        parts.Clear();
        if (side.Owner == TradeSideOwner.Own)
        {
            Own = whole;
        }
        else
        {
            Theirs = whole;
        }

        return true;
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
        if (Requester != null)
        {
            m_answered = Requester;
            m_answeredEndsAt = RequestEndsAt;
        }

        Requester = null;
    }

    /// <summary>
    ///     Asks again the request last answered, which the server keeps when the answer came while an item change was
    ///     still going through (Gameplay Systems §16); it runs out when it would have.
    /// </summary>
    public void ReopenRequest(string requester)
    {
        if (Requester == null && !IsOpen && m_answered == requester)
        {
            Requester = requester;
            RequestEndsAt = m_answeredEndsAt;
        }
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

    private sealed class SideParts
    {
        public List<TradeEntry> Rows { get; } = new();

        public int Next { get; set; }

        public int Count { get; set; }

        public void Clear()
        {
            Rows.Clear();
            Next = 0;
            Count = 0;
        }
    }
}
}
