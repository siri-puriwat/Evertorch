using System;

namespace Evertorch.Game
{
/// <summary>
///     The attempt that reaches one refine level (Content Pipeline §4): its chance in percent, its coins, and the bonus
///     the item has at that level, which counts every level below it.
/// </summary>
public sealed class RefineStep
{
    public RefineStep(int chance, int coins, int bonus)
    {
        if (chance < 0 || coins < 0 || bonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chance), "Refine step values cannot be negative.");
        }

        Chance = chance;
        Coins = coins;
        Bonus = bonus;
    }

    public int Chance { get; }

    public int Coins { get; }

    public int Bonus { get; }
}
}
