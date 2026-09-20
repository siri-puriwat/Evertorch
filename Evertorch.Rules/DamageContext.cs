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
        IRandomSource random)
    {
        if (statusAttack < 0 || weaponAttack < 0 || hardDefense < 0 || softDefense < 0)
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
}
}
