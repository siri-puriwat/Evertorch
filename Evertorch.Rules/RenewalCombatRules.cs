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
        int hitChance = Math.Max(
            MinHitChancePercent,
            Math.Min(MaxHitChancePercent, context.AttackerHit - context.DefenderFlee));

        // Every stage reached draws exactly one number, even at zero chance, so a seed replays identically
        // whatever the statistics are.
        if (context.Random.Next(PermilleRange) < context.DefenderPerfectDodge)
        {
            return new HitResult(HitOutcome.PerfectDodge, hitChance);
        }

        int criticalChance = context.AttackerCritical - 2 * context.DefenderLuk;
        if (context.Random.Next(PermilleRange) < criticalChance)
        {
            return new HitResult(HitOutcome.Critical, hitChance);
        }

        HitOutcome outcome = context.Random.Next(PercentRange) < hitChance ? HitOutcome.Hit : HitOutcome.Miss;
        return new HitResult(outcome, hitChance);
    }

    public DamageResult CalculateDamage(DamageContext context)
    {
        long raw = context.AttackerKind == AttackerKind.Character
            ? 2L * context.StatusAttack + RollWeaponAttack(context)
            : RollMonsterAttack(context) + context.StatusAttack;
        raw = raw * context.RatioPercent / 100;

        long reduced = raw * (DefenseScale + context.HardDefense) / (DefenseScale + 10L * context.HardDefense)
            - context.SoftDefense;
        long amount = Math.Max(1, reduced);
        if (context.IsCritical)
        {
            amount = amount * 14 / 10;
        }

        return new DamageResult((int)Math.Min(int.MaxValue, amount), context.IsCritical);
    }

    /// <summary>
    ///     Magic as the skills research note reads it: the magic attack rolled between 80 % and 120 %, the ratio, then
    ///     hard magic defense as a ratio and soft magic defense subtracted, at least 1. Magic never misses or crits.
    /// </summary>
    public DamageResult CalculateMagicDamage(MagicDamageContext context)
    {
        int min = context.MagicAttack * 80 / 100;
        int max = context.MagicAttack * 120 / 100;
        long raw = (min + context.Random.Next(max - min + 1)) * (long)context.RatioPercent / 100;
        long reduced = raw * (MagicDefenseScale + context.HardMagicDefense)
            / (MagicDefenseScale + 10L * context.HardMagicDefense)
            - context.SoftMagicDefense;
        return new DamageResult((int)Math.Min(int.MaxValue, Math.Max(1, reduced)), false);
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
