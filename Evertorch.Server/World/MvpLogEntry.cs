using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     What one character has done to a boss and suffered from it in its life (Gameplay Systems §10): the damage it
///     dealt the boss, by any means, and the damage the boss's basic attacks dealt it.
/// </summary>
public readonly struct MvpLogEntry
{
    public MvpLogEntry(CharacterId character, long dealt, long taken)
    {
        Character = character;
        Dealt = dealt;
        Taken = taken;
    }

    public CharacterId Character { get; }

    public long Dealt { get; }

    public long Taken { get; }

    public long Total => Dealt + Taken;
}
}
