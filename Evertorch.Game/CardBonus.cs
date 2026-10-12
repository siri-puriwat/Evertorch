using System;

namespace Evertorch.Game
{
/// <summary>
///     What a card adds while the item holding it is worn (Gameplay Systems §11.1, §11.7): primary statistics; a flat
///     attack, magic attack, defense, max HP, and max SP; and the boss modifiers, in percent. Server-only content.
/// </summary>
public sealed class CardBonus
{
    public static readonly CardBonus None = new(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, 0, 0);

    public CardBonus(
        PrimaryStats stats,
        int attack,
        int magicAttack,
        int defense,
        int maxHp,
        int maxSp,
        int bossDamagePercent,
        int bossDamageReductionPercent)
    {
        if (attack < 0
            || magicAttack < 0
            || defense < 0
            || maxHp < 0
            || maxSp < 0
            || bossDamagePercent < 0
            || bossDamageReductionPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attack), "A card's values cannot be negative.");
        }

        Stats = stats;
        Attack = attack;
        MagicAttack = magicAttack;
        Defense = defense;
        MaxHp = maxHp;
        MaxSp = maxSp;
        BossDamagePercent = bossDamagePercent;
        BossDamageReductionPercent = bossDamageReductionPercent;
    }

    /// <summary>Added to the wearer's primary statistics.</summary>
    public PrimaryStats Stats { get; }

    public int Attack { get; }

    public int MagicAttack { get; }

    /// <summary>Added to the wearer's hard defense.</summary>
    public int Defense { get; }

    /// <summary>Added to the wearer's max HP after the VIT scaling.</summary>
    public int MaxHp { get; }

    /// <summary>Added to the wearer's max SP after the INT scaling.</summary>
    public int MaxSp { get; }

    /// <summary>More damage to a boss, in percent.</summary>
    public int BossDamagePercent { get; }

    /// <summary>Less damage from a boss, in percent; combat caps the sum at 90.</summary>
    public int BossDamageReductionPercent { get; }

    /// <summary>
    ///     Both bonuses together, as the cards of every worn row add up (Gameplay Systems §11.1).
    /// </summary>
    public CardBonus Plus(CardBonus other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        return new CardBonus(
            new PrimaryStats(
                Stats.Str + other.Stats.Str,
                Stats.Agi + other.Stats.Agi,
                Stats.Vit + other.Stats.Vit,
                Stats.Int + other.Stats.Int,
                Stats.Dex + other.Stats.Dex,
                Stats.Luk + other.Stats.Luk),
            Attack + other.Attack,
            MagicAttack + other.MagicAttack,
            Defense + other.Defense,
            MaxHp + other.MaxHp,
            MaxSp + other.MaxSp,
            BossDamagePercent + other.BossDamagePercent,
            BossDamageReductionPercent + other.BossDamageReductionPercent);
    }
}
}
