using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     The damage one character has dealt a monster in its life, the killing blow's overkill included.
/// </summary>
public readonly struct DamageLogEntry
{
    public DamageLogEntry(CharacterId character, long damage)
    {
        Character = character;
        Damage = damage;
    }

    public CharacterId Character { get; }

    public long Damage { get; }
}
}
