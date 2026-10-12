using System;

namespace Evertorch.Rules
{
/// <summary>
///     The last step of every damage path (Gameplay Systems §11.1): damage dealt to a boss grows by the attacker's worn
///     cards' percentage, and damage a boss deals shrinks by the defender's, at most 90 %. Each is rounded down and
///     leaves at least 1; the default changes nothing and draws nothing.
/// </summary>
public readonly struct DamageModifiers
{
    /// <summary>
    ///     The most a defender's cards take off a boss's damage, in percent.
    /// </summary>
    public const int MaxBossDamageReductionPercent = 90;

    public DamageModifiers(int bossDamagePercent, int bossDamageReductionPercent)
    {
        if (bossDamagePercent < 0 || bossDamageReductionPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bossDamagePercent), "Modifiers cannot be negative.");
        }

        BossDamagePercent = bossDamagePercent;
        BossDamageReductionPercent = bossDamageReductionPercent;
    }

    /// <summary>More damage to a boss, in percent: the attacker's cards' sum when the target is a boss, else 0.</summary>
    public int BossDamagePercent { get; }

    /// <summary>
    ///     Less damage from a boss, in percent: the defender's cards' sum when the attacker is a boss, else 0.
    /// </summary>
    public int BossDamageReductionPercent { get; }

    /// <summary>
    ///     <paramref name="amount" /> after both modifiers, each rounded down, and at least 1.
    /// </summary>
    public long Apply(long amount)
    {
        if (BossDamagePercent > 0)
        {
            amount = amount * (100 + BossDamagePercent) / 100;
        }

        if (BossDamageReductionPercent > 0)
        {
            amount = amount * (100 - Math.Min(MaxBossDamageReductionPercent, BossDamageReductionPercent)) / 100;
        }

        return Math.Max(1, amount);
    }
}
}
