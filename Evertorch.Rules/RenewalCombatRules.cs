using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     Renewal-inspired basic-attack timing, hit resolution, and physical damage. Outputs are values; nothing here
///     mutates an entity or emits an event.
/// </summary>
public sealed class RenewalCombatRules : ICombatRules
{
    public const int MinHitChancePercent = 5;
    public const int MaxHitChancePercent = 100;

    private const int ZeroSpeedMotionMs = 2000;
    private const int MotionMsPerAttackSpeed = 10;
    private const int PermilleRange = 1000;
    private const int PercentRange = 100;
    private const int DefenseScale = 4000;
    private const int MagicDefenseScale = 1000;

    public AttackTiming CalculateAttackTiming(AttackContext context)
    {
        TimeSpan motion;
        if (context.HasFixedInterval)
        {
            motion = TimeSpan.FromTicks(context.FixedInterval.Ticks / 2);
        }
        else
        {
            int attackSpeed = Math.Min(context.AttackSpeed, RenewalCharacterRules.MaxAttackSpeed);
            motion = TimeSpan.FromMilliseconds(ZeroSpeedMotionMs - MotionMsPerAttackSpeed * attackSpeed);
        }

        // The attack lands when the motion ends and the next may start one further motion later. Recovery, the
        // part of that gap which still restricts movement, is Evertorch tuning rather than reference behaviour.
        var interval = TimeSpan.FromTicks(motion.Ticks * 2);
        var recovery = TimeSpan.FromTicks((interval.Ticks - motion.Ticks) / 2);
        return new AttackTiming(interval, motion, motion, recovery);
    }

    public HitResult CalculateHit(HitContext context)
    {
        int hit = context.IsAttackerBlinded ? LessAQuarter(context.AttackerHit) : context.AttackerHit;
        int flee = context.IsDefenderBlinded ? LessAQuarter(context.DefenderFlee) : context.DefenderFlee;
        int hitChance = Math.Max(MinHitChancePercent, Math.Min(MaxHitChancePercent, hit - flee));

        // Every stage reached draws exactly one number, even at zero chance, so a seed replays identically
        // whatever the statistics are.
        if (context.Random.Next(PermilleRange) < context.DefenderPerfectDodge)
        {
            return new HitResult(HitOutcome.PerfectDodge, hitChance);
        }

        int criticalChance = context.AttackerCritical - 2 * context.DefenderLuk;
        if (context.IsDefenderAsleep)
        {
            criticalChance *= 2;
        }

        if (context.Random.Next(PermilleRange) < criticalChance)
        {
            return new HitResult(HitOutcome.Critical, hitChance);
        }

        // A held body is always hit; the roll still draws, so a seed replays whatever holds whom.
        bool isHit = context.Random.Next(PercentRange) < hitChance || context.IsDefenderHeld;
        return new HitResult(isHit ? HitOutcome.Hit : HitOutcome.Miss, hitChance);
    }

    public DamageResult CalculateDamage(DamageContext context)
    {
        DamageModifiers modifiers = context.Modifiers;
        long raw;
        if (context.AttackerKind == AttackerKind.Character)
        {
            // Three parts (Gameplay Systems §11.1): the status attack, always Neutral and doubled after its element;
            // the weapon's draw, sized and then by the attack's element; and the flat attack, the refine bonus and the
            // cards' attack, which no draw varies, by the attack's element alone. The cards' damage to the target
            // grows the last two.
            long status = (long)context.StatusAttack * ElementPercent(modifiers, Element.Neutral) / 100;
            int attackPercent = ElementPercent(modifiers, modifiers.AttackElement ?? Element.Neutral);
            long weapon = RollWeaponAttack(context) * SizePercent(modifiers.Weapon, modifiers.DefenderSize) / 100
                * attackPercent / 100;
            long flat = (long)context.FlatAttack * attackPercent / 100;
            raw = 2 * status + modifiers.Dealt(weapon + flat);
            if (raw == 0 && modifiers.AttackElement.HasValue)
            {
                return Immune(context.IsCritical);
            }
        }
        else
        {
            raw = RollMonsterAttack(context) + context.StatusAttack;
        }

        raw = raw * context.RatioPercent / 100;
        long hardDefense = context.HardDefense;
        long softDefense = context.SoftDefense;
        if (context.IsDefenderFrozen)
        {
            hardDefense /= 2;
        }

        // A monster has hard defense alone, a player both; poison cuts the part that varies with the defender's own
        // body, after the freeze's half (Gameplay Systems §9.1).
        if (context.IsDefenderPoisoned && context.AttackerKind == AttackerKind.Character)
        {
            hardDefense -= hardDefense * 25 / 100;
        }
        else if (context.IsDefenderPoisoned)
        {
            softDefense -= softDefense * 25 / 100;
        }

        long reduced = raw * (DefenseScale + hardDefense) / (DefenseScale + 10L * hardDefense) - softDefense;
        long amount = Math.Max(1, reduced);
        if (context.IsCritical)
        {
            amount = amount * 14 / 10;
        }

        // A monster's skill takes its element after the defense, and its swing none (Gameplay Systems §9).
        if (context.AttackerKind == AttackerKind.Monster && modifiers.AttackElement.HasValue)
        {
            int percent = ElementPercent(modifiers, modifiers.AttackElement.Value);
            if (percent == 0)
            {
                return Immune(context.IsCritical);
            }

            amount = Math.Max(1, amount * percent / 100);
        }

        amount = modifiers.Taken(amount);
        return new DamageResult((int)Math.Min(int.MaxValue, amount), context.IsCritical);
    }

    /// <summary>
    ///     Magic as the skills research note reads it: the magic attack rolled between 80 % and 120 %, the ratio, then
    ///     hard magic defense as a ratio and soft magic defense subtracted, at least 1; then the skill's element against
    ///     the target's, where a cell of 0 is immune and any other keeps at least 1; then the defender's cards'
    ///     reductions. No card adds to magic (owner decisions 13 and 21), and magic never misses or crits.
    /// </summary>
    public DamageResult CalculateMagicDamage(MagicDamageContext context)
    {
        int min = context.MagicAttack * 80 / 100;
        int max = context.MagicAttack * 120 / 100;
        long raw = (min + context.Random.Next(max - min + 1)) * (long)context.RatioPercent / 100;
        long reduced = raw * (MagicDefenseScale + context.HardMagicDefense)
            / (MagicDefenseScale + 10L * context.HardMagicDefense)
            - context.SoftMagicDefense;
        long amount = Math.Max(1, reduced);
        DamageModifiers modifiers = context.Modifiers;
        if (modifiers.AttackElement.HasValue)
        {
            int percent = ElementPercent(modifiers, modifiers.AttackElement.Value);
            if (percent == 0)
            {
                return Immune(false);
            }

            amount = Math.Max(1, amount * percent / 100);
        }

        amount = modifiers.Taken(amount);
        return new DamageResult((int)Math.Min(int.MaxValue, amount), false);
    }

    /// <summary>
    ///     What a player's rolled weapon attack deals a target of <paramref name="size" />, in percent (Gameplay Systems
    ///     §11.1): a sword 75 against small and large targets, and a staff and bare hands 100 against all.
    /// </summary>
    public static int SizePercent(WeaponType? weapon, BodySize size)
    {
        return weapon == WeaponType.Sword && size != BodySize.Medium ? 75 : 100;
    }

    // Blind's cut of a HIT or a FLEE (Gameplay Systems §9.1).
    private static int LessAQuarter(int value)
    {
        return value - value * 25 / 100;
    }

    // The element table's cell for a part of the given element against the defender's; without an attack element
    // there is no element step at all.
    private static int ElementPercent(DamageModifiers modifiers, Element part)
    {
        return modifiers.AttackElement.HasValue ? ElementTable.Percent(part, modifiers.DefenseElement) : 100;
    }

    private static DamageResult Immune(bool isCritical)
    {
        return new DamageResult(0, isCritical, true);
    }

    // An armed character's swing draws once more (equipment research note): every weapon is weapon level 1 and varies
    // by 5 % of its attack either way, both bounds included; a critical takes the top without a draw. A fist draws
    // nothing, so every unarmed vector holds.
    private static long RollWeaponAttack(DamageContext context)
    {
        int attack = context.WeaponAttack;
        if (attack == 0)
        {
            return 0;
        }

        int variance = attack * 5 / 100;
        return context.IsCritical ? attack + variance : attack - variance + context.Random.Next(2 * variance + 1);
    }

    private static long RollMonsterAttack(DamageContext context)
    {
        int min = context.WeaponAttack * 80 / 100;
        int max = context.WeaponAttack * 120 / 100;
        if (context.IsCritical)
        {
            return max;
        }

        return min + context.Random.Next(max - min + 1);
    }
}
}
