namespace Evertorch.Protocol
{
/// <summary>
///     What a <see cref="MarketEvent" /> tells its player (Network Protocol §6).
/// </summary>
public enum MarketEventKind : byte
{
    None = 0,

    /// <summary>
    ///     The player's listing is on the board: the units listed and the fee paid.
    /// </summary>
    Listed = 1,

    /// <summary>
    ///     Units of the player's listing sold: the units and their gross, which wait on the listing.
    /// </summary>
    Sold = 2,

    /// <summary>
    ///     The player collected a listing: the units given back and the net coins.
    /// </summary>
    Collected = 3
}
}
