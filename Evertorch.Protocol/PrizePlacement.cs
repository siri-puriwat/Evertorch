namespace Evertorch.Protocol
{
/// <summary>
///     Where an <see cref="MvpAwarded" /> prize went (Network Protocol §9): nowhere when the boss gave none, into the
///     bag, or to the player's feet when the bag was full.
/// </summary>
public enum PrizePlacement : byte
{
    None = 0,
    Bag = 1,
    Feet = 2
}
}
