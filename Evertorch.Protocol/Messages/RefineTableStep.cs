namespace Evertorch.Protocol
{
/// <summary>
///     One level of a <see cref="RefineTable" />: the chance, in percent, of an attempt reaching it, and its coins.
/// </summary>
public readonly struct RefineTableStep
{
    public RefineTableStep(byte chance, uint coins)
    {
        Chance = chance;
        Coins = coins;
    }

    public byte Chance { get; }

    public uint Coins { get; }
}
}
