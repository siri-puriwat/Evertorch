namespace Evertorch.Protocol
{
/// <summary>
///     What a <see cref="TradeEvent" /> tells (Gameplay Systems §16), and whose name it carries: the requester for a
///     request; the partner for a decline, an expiry, an opening, a cancel, or a completion; and, for a failure, the side
///     the commit found wanting. Zero is never sent.
/// </summary>
public enum TradeEventKind : byte
{
    None = 0,
    Requested = 1,
    Declined = 2,
    Expired = 3,
    Opened = 4,
    Cancelled = 5,
    Completed = 6,

    /// <summary>
    ///     The commit refused the trade, which ended with nothing moved; the event's reason says why.
    /// </summary>
    Failed = 7,

    /// <summary>
    ///     The commit could not start, so the trade stays open with both confirms cleared; the reason is 6.
    /// </summary>
    Unsaved = 8
}
}
