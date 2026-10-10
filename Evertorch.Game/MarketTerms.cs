namespace Evertorch.Game
{
/// <summary>
///     The market board's fixed terms (Gameplay Systems §11.5): the listing fee and the tax, the listings a character
///     holds at most, and how big a price's step is. The periods are the server's options.
/// </summary>
public static class MarketTerms
{
    /// <summary>
    ///     The fee to list, in percent of the units' value at the board's price, rounded down.
    /// </summary>
    public const int FeePercent = 1;

    /// <summary>
    ///     The least a listing costs, however little it is worth.
    /// </summary>
    public const int MinimumFee = 1;

    /// <summary>
    ///     What the board keeps of a listing's sales when its seller collects them, in percent, rounded down once.
    /// </summary>
    public const int TaxPercent = 9;

    public const int MaxListings = 8;

    /// <summary>
    ///     A step is the price over this, rounded down, and at least 1 coin.
    /// </summary>
    public const int StepDivisor = 10;

    /// <summary>
    ///     A ceiling the content leaves out is this many times the base, within what a stack may cost.
    /// </summary>
    public const int CeilingFactor = 10;
}
}
