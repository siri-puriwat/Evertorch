using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     What a card adds while the item holding it is worn (Gameplay Systems §11.1, §11.7): primary statistics; a flat
///     attack, magic attack, defense, max HP, and max SP; the boss modifiers, in percent; the damage it adds to a race,
///     an element, or a size and takes off an element, in percent by name; the element an armor card gives its wearer;
///     and the ailments a weapon card may bring on a hit and a card of any other category resists (§9.1). Server-only
///     content.
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
        int bossDamageReductionPercent,
        IReadOnlyDictionary<Race, int>? raceDamage = null,
        IReadOnlyDictionary<Element, int>? elementDamage = null,
        IReadOnlyDictionary<BodySize, int>? sizeDamage = null,
        IReadOnlyDictionary<Element, int>? elementDamageReduction = null,
        Element armorElement = Element.Neutral,
        IReadOnlyDictionary<Ailment, int>? inflict = null,
        IReadOnlyDictionary<Ailment, int>? resist = null)
    {
        if (attack < 0
            || magicAttack < 0
            || defense < 0
            || maxHp < 0
            || maxSp < 0
            || bossDamagePercent < 0
            || bossDamageReductionPercent < 0
            || HasNegative(raceDamage)
            || HasNegative(elementDamage)
            || HasNegative(sizeDamage)
            || HasNegative(elementDamageReduction)
            || HasNegative(inflict)
            || HasNegative(resist))
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
        RaceDamage = raceDamage ?? NoValues<Race>.Map;
        ElementDamage = elementDamage ?? NoValues<Element>.Map;
        SizeDamage = sizeDamage ?? NoValues<BodySize>.Map;
        ElementDamageReduction = elementDamageReduction ?? NoValues<Element>.Map;
        ArmorElement = armorElement;
        Inflict = inflict ?? NoValues<Ailment>.Map;
        Resist = resist ?? NoValues<Ailment>.Map;
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

    /// <summary>More damage to a boss, in percent, on physical hits alone (owner decision 21).</summary>
    public int BossDamagePercent { get; }

    /// <summary>Less damage from a boss, in percent; combat caps the sum at 90.</summary>
    public int BossDamageReductionPercent { get; }

    /// <summary>More damage to a race, in percent by race, on physical hits alone (owner decision 13).</summary>
    public IReadOnlyDictionary<Race, int> RaceDamage { get; }

    /// <summary>More damage to a defender's element, in percent by that element, on physical hits alone.</summary>
    public IReadOnlyDictionary<Element, int> ElementDamage { get; }

    /// <summary>More damage to a size, in percent by size, on physical hits alone.</summary>
    public IReadOnlyDictionary<BodySize, int> SizeDamage { get; }

    /// <summary>Less damage of an attack's element, in percent by that element; combat caps the sum at 90.</summary>
    public IReadOnlyDictionary<Element, int> ElementDamageReduction { get; }

    /// <summary>
    ///     The element an armor card gives its wearer; Neutral for a card that gives none, so the first card in slot
    ///     order that gives one wins.
    /// </summary>
    public Element ArmorElement { get; }

    /// <summary>
    ///     The chance a weapon card brings each ailment on its wearer's physical hits, in basis points by ailment; combat
    ///     caps the worn cards' sum of one ailment at 10,000.
    /// </summary>
    public IReadOnlyDictionary<Ailment, int> Inflict { get; }

    /// <summary>
    ///     How much a card that fits anything but a weapon resists each ailment, in percent by ailment; combat caps the
    ///     worn cards' sum at 100.
    /// </summary>
    public IReadOnlyDictionary<Ailment, int> Resist { get; }

    /// <summary>
    ///     Both bonuses together, as the cards of every worn row add up (Gameplay Systems §11.1): every value summed, by
    ///     name where it has one, and this bonus's armor element unless it gives none.
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
            BossDamageReductionPercent + other.BossDamageReductionPercent,
            Summed(RaceDamage, other.RaceDamage),
            Summed(ElementDamage, other.ElementDamage),
            Summed(SizeDamage, other.SizeDamage),
            Summed(ElementDamageReduction, other.ElementDamageReduction),
            ArmorElement != Element.Neutral ? ArmorElement : other.ArmorElement,
            Summed(Inflict, other.Inflict),
            Summed(Resist, other.Resist));
    }

    /// <summary>The percentage this bonus adds to the damage dealt to <paramref name="race" />.</summary>
    public int DamageTo(Race race)
    {
        return RaceDamage.TryGetValue(race, out int percent) ? percent : 0;
    }

    /// <summary>The percentage this bonus adds to the damage dealt to a defender of <paramref name="element" />.</summary>
    public int DamageTo(Element element)
    {
        return ElementDamage.TryGetValue(element, out int percent) ? percent : 0;
    }

    /// <summary>The percentage this bonus adds to the damage dealt to <paramref name="size" />.</summary>
    public int DamageTo(BodySize size)
    {
        return SizeDamage.TryGetValue(size, out int percent) ? percent : 0;
    }

    /// <summary>The percentage this bonus takes off the damage of an attack of <paramref name="element" />.</summary>
    public int ReductionFrom(Element element)
    {
        return ElementDamageReduction.TryGetValue(element, out int percent) ? percent : 0;
    }

    /// <summary>The chance, in basis points, this bonus brings <paramref name="ailment" /> on a hit.</summary>
    public int ChanceToInflict(Ailment ailment)
    {
        return Inflict.TryGetValue(ailment, out int basisPoints) ? basisPoints : 0;
    }

    /// <summary>The percentage this bonus resists <paramref name="ailment" /> by.</summary>
    public int ResistanceTo(Ailment ailment)
    {
        return Resist.TryGetValue(ailment, out int percent) ? percent : 0;
    }

    private static bool HasNegative<TKey>(IReadOnlyDictionary<TKey, int>? values)
    {
        if (values == null)
        {
            return false;
        }

        foreach (int value in values.Values)
        {
            if (value < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<TKey, int> Summed<TKey>(
        IReadOnlyDictionary<TKey, int> left,
        IReadOnlyDictionary<TKey, int> right)
        where TKey : notnull
    {
        var sum = new Dictionary<TKey, int>();
        foreach (KeyValuePair<TKey, int> entry in left)
        {
            sum[entry.Key] = entry.Value;
        }

        foreach (KeyValuePair<TKey, int> entry in right)
        {
            sum[entry.Key] = sum.TryGetValue(entry.Key, out int value) ? value + entry.Value : entry.Value;
        }

        return sum;
    }

    // A nested holder, so None never meets an empty map before it exists, whatever order the static fields keep.
    private static class NoValues<TKey>
        where TKey : notnull
    {
        public static readonly IReadOnlyDictionary<TKey, int> Map = new Dictionary<TKey, int>();
    }
}
}
