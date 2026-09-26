using System;

namespace Evertorch.Game
{
/// <summary>
///     What a consumable restores once its use is committed (Gameplay Systems §11.2): flat amounts of HP and SP, each
///     capped at its maximum. Server-only content.
/// </summary>
public sealed class ItemEffect
{
    public ItemEffect(int health, int spirit)
    {
        if (health < 0 || spirit < 0 || (health == 0 && spirit == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(health), "A consumable restores HP, SP, or both.");
        }

        Health = health;
        Spirit = spirit;
    }

    public int Health { get; }

    public int Spirit { get; }
}
}
