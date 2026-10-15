namespace Evertorch.Game
{
/// <summary>
///     The six ailments a status may be (Gameplay Systems §9.1); None for every other status.
/// </summary>
public enum Ailment : byte
{
    None = 0,
    Poison = 1,
    Stun = 2,
    Freeze = 3,
    Sleep = 4,
    Silence = 5,
    Blind = 6
}
}
