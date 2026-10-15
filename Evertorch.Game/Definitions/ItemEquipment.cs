using System;

namespace Evertorch.Game
{
/// <summary>
///     What a worn item adds (Gameplay Systems §11.1): a weapon's attack, attack-speed penalty, and type, any other
///     piece's defense, a shield's attack-speed penalty, and primary-statistic bonuses on any. Server-only content.
/// </summary>
public sealed class ItemEquipment
{
    public ItemEquipment(
        int attack,
        int attackSpeedPenalty,
        int defense,
        PrimaryStats bonus,
        WeaponType? weaponType = null)
    {
        if (attack < 0 || attackSpeedPenalty < 0 || defense < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attack), "Equipment values cannot be negative.");
        }

        Attack = attack;
        AttackSpeedPenalty = attackSpeedPenalty;
        Defense = defense;
        Bonus = bonus;
        WeaponType = weaponType;
    }

    public int Attack { get; }

    /// <summary>
    ///     A weapon's replaces the job's unarmed penalty while it is worn, and a shield's adds to whichever counts
    ///     (Gameplay Systems §2); 0 on any other piece.
    /// </summary>
    public int AttackSpeedPenalty { get; }

    /// <summary>The wearer's hard defense.</summary>
    public int Defense { get; }

    /// <summary>Added to the wearer's primary statistics.</summary>
    public PrimaryStats Bonus { get; }

    /// <summary>A weapon's type, which decides the jobs that can wield it; null for any other piece.</summary>
    public WeaponType? WeaponType { get; }
}
}
