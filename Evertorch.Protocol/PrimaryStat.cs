namespace Evertorch.Protocol
{
/// <summary>
///     A primary statistic a command names (Gameplay Systems §2), in the order <see cref="CharacterSheet" /> lists them.
///     Zero is never sent.
/// </summary>
public enum PrimaryStat : byte
{
    None = 0,
    Str = 1,
    Agi = 2,
    Vit = 3,
    Int = 4,
    Dex = 5,
    Luk = 6
}
}
