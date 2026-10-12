using System;

namespace Evertorch.Rules
{
public readonly struct DamageContext
{
    public DamageContext(
        AttackerKind attackerKind,
        int statusAttack,
        int weaponAttack,
        int hardDefense,
        int softDefense,
        bool isCritical,
        IRandomSource random,
        int ratioPercent = 100,
        int flatAttack = 0,
        DamageModifiers modifiers = default)
    {
        if (statusAttack < 0
            || weaponAttack < 0
            || hardDefense < 0
            || softDefense < 0
            || ratioPercent < 0
            || flatAttack < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(statusAttack), "Damage inputs cannot be negative.");
        }

        AttackerKind = attackerKind;
        StatusAttack = statusAttack;
        WeaponAttack = weaponAttack;
        HardDefense = hardDefense;
        SoftDefense = softDefense;
        IsCritical = isCritical;
        Random = random ?? throw new ArgumentNullException(nameof(random));
        RatioPercent = ratioPercent;
        FlatAttack = flatAttack;
        Modifiers = modifiers;
    }

    public AttackerKind AttackerKind { get; }

    /// <summary>The derived physical attack of a character. Zero for monsters, which define one attack value.</summary>
    public int StatusAttack { get; }

    /// <summary>Attack of the equipped weapon, zero when unarmed; for a monster, its defined physical attack.</summary>
    public int WeaponAttack { get; }

    /// <summary>Equipment or definition defense, applied as a ratio.</summary>
    public int HardDefense { get; }

    /// <summary>Stat-derived defense, subtracted after the ratio.</summary>
    public int SoftDefense { get; }

    public bool IsCritical { get; }

    public IRandomSource Random { get; }

    /// <summary>
    ///     A skill's share of the raw damage, applied before defense; 100 for a basic attack.
    /// </summary>
    public int RatioPercent { get; }

    /// <summary>
    ///     A character's attack added after its weapon's draw, which no draw varies: its weapon's refine bonus and its worn
    ///     cards' attack (Gameplay Systems §11.1). Never the weapon's own attack; 0 for a monster.
    /// </summary>
    public int FlatAttack { get; }

    /// <summary>
    ///     The boss modifiers, the last step of the damage; the default changes nothing.
    /// </summary>
    public DamageModifiers Modifiers { get; }

    /// <summary>
    ///     The same inputs for a skill's damage: never critical, at the skill's share of the raw damage (Gameplay
    ///     Systems §9), every other input kept.
    /// </summary>
    public DamageContext ForSkill(int ratioPercent)
    {
        return new DamageContext(
            AttackerKind,
            StatusAttack,
            WeaponAttack,
            HardDefense,
            SoftDefense,
            false,
            Random,
            ratioPercent,
            FlatAttack,
            Modifiers);
    }
}
}
