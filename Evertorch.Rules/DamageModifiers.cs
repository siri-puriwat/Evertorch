using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     What the elements, the target's size, and the worn cards make of a hit (Gameplay Systems §3, §11.1): the attack's
///     element and the defender's, the attacking player's weapon and the defender's size, the attacker's cards' damage
///     to the defender's element, race, and size and to a boss, and the defender's cards' reduction for the attack's
///     element and from a boss. Both damage contexts carry it and copy it to a skill; the default changes nothing and
///     draws nothing.
/// </summary>
public readonly struct DamageModifiers
{
    /// <summary>
    ///     The most a defender's cards take off a hit, in percent: for the attack's element and from a boss alike.
    /// </summary>
    public const int MaxReductionPercent = 90;

    public DamageModifiers(
        int bossDamagePercent,
        int bossDamageReductionPercent,
        Element? attackElement = null,
        Element defenseElement = Element.Neutral,
        WeaponType? weapon = null,
        BodySize defenderSize = BodySize.Medium,
        int elementDamagePercent = 0,
        int raceDamagePercent = 0,
        int sizeDamagePercent = 0,
        int elementDamageReductionPercent = 0)
    {
        if (bossDamagePercent < 0
            || bossDamageReductionPercent < 0
            || elementDamagePercent < 0
            || raceDamagePercent < 0
            || sizeDamagePercent < 0
            || elementDamageReductionPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bossDamagePercent), "Modifiers cannot be negative.");
        }

        BossDamagePercent = bossDamagePercent;
        BossDamageReductionPercent = bossDamageReductionPercent;
        AttackElement = attackElement;
        DefenseElement = defenseElement;
        Weapon = weapon;
        DefenderSize = defenderSize;
        ElementDamagePercent = elementDamagePercent;
        RaceDamagePercent = raceDamagePercent;
        SizeDamagePercent = sizeDamagePercent;
        ElementDamageReductionPercent = elementDamageReductionPercent;
    }

    /// <summary>
    ///     More damage to a boss, in percent: the attacking player's cards' sum when the target is a boss, else 0. It acts
    ///     on a physical hit's weapon and flat parts alone (owner decision 21).
    /// </summary>
    public int BossDamagePercent { get; }

    /// <summary>
    ///     Less damage from a boss, in percent: the defending player's cards' sum when the attacker is a boss, else 0.
    /// </summary>
    public int BossDamageReductionPercent { get; }

    /// <summary>
    ///     The attack's element; null skips the element step, as a monster's swing and every earlier vector do.
    /// </summary>
    public Element? AttackElement { get; }

    /// <summary>The defender's element: a monster's own, or a player's armor element.</summary>
    public Element DefenseElement { get; }

    /// <summary>The attacking player's weapon, which meets the defender's size; null for bare hands and monsters.</summary>
    public WeaponType? Weapon { get; }

    public BodySize DefenderSize { get; }

    /// <summary>More damage to the defender's element, in percent: the attacking player's cards' sum.</summary>
    public int ElementDamagePercent { get; }

    /// <summary>More damage to the defender's race, in percent: the attacking player's cards' sum.</summary>
    public int RaceDamagePercent { get; }

    /// <summary>More damage to the defender's size, in percent: the attacking player's cards' sum.</summary>
    public int SizeDamagePercent { get; }

    /// <summary>Less damage of the attack's element, in percent: the defending player's cards' sum.</summary>
    public int ElementDamageReductionPercent { get; }

    /// <summary>
    ///     A physical hit's weapon and flat parts after the attacker's cards (owner decisions 13 and 21): the defender's
    ///     element, its race, its size, then a boss, each × (100 + p) ÷ 100 and rounded down.
    /// </summary>
    public long Dealt(long parts)
    {
        parts = Grown(parts, ElementDamagePercent);
        parts = Grown(parts, RaceDamagePercent);
        parts = Grown(parts, SizeDamagePercent);
        return Grown(parts, BossDamagePercent);
    }

    /// <summary>
    ///     A hit's amount after the defender's cards: the attack's element, then a boss, each × (100 − min(90, p)) ÷ 100,
    ///     rounded down, and at least 1.
    /// </summary>
    public long Taken(long amount)
    {
        amount = Shrunk(amount, ElementDamageReductionPercent);
        return Shrunk(amount, BossDamageReductionPercent);
    }

    private static long Grown(long amount, int percent)
    {
        return percent > 0 ? amount * (100 + percent) / 100 : amount;
    }

    private static long Shrunk(long amount, int percent)
    {
        return percent > 0 ? Math.Max(1, amount * (100 - Math.Min(MaxReductionPercent, percent)) / 100) : amount;
    }
}
}
