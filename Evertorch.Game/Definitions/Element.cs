namespace Evertorch.Game
{
/// <summary>
///     The ten elements an attack, a monster, or a player's armor has (Gameplay Systems §3); the values are the ones on
///     the wire.
/// </summary>
public enum Element : byte
{
    Neutral = 0,
    Water = 1,
    Earth = 2,
    Fire = 3,
    Wind = 4,
    Poison = 5,
    Holy = 6,
    Shadow = 7,
    Ghost = 8,
    Undead = 9
}
}
