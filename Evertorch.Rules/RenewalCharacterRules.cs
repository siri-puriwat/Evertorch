using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
/// Renewal-inspired derived statistics. Every formula is whole-number arithmetic with a fixed truncation order,
/// so results are identical on every platform; see the private research note for derivations and vectors.
/// </summary>
public sealed class RenewalCharacterRules : ICharacterRules
{
    public const int MaxAttackSpeed = 190;

    private const int AttackSpeedBase = 196;
    private const int VariableCastStatScale = 530;
    private const int FullCastPermille = 1000;

    public DerivedStats CalculateDerivedStats(CharacterBuild build)
    {
        int level = build.BaseLevel;
        PrimaryStats stats = build.Stats;

        // Each stat term truncates on its own before the sum is scaled back down.
        int physicalAttack = ((stats.Str * 10) + (stats.Dex * 10 / 5) + (stats.Luk * 10 / 3) + (level * 10 / 4)) / 10;
        int magicalAttack = stats.Int + (stats.Int / 2) + (stats.Dex / 5) + (stats.Luk / 3) + (level / 4);

        // The defenses truncate once, after their fractional terms are summed; a common denominator keeps that
        // exact without floating point.
        int softDefense = ((5 * (level + stats.Vit)) + (2 * stats.Agi)) / 10;
        int softMagicDefense = stats.Int + (((5 * level) + (4 * (stats.Dex + stats.Vit))) / 20);

        int hit = level + stats.Dex + (stats.Luk / 3) + 175;
        int flee = level + stats.Agi + (stats.Luk / 5) + 100;
        int critical = (level / 10) + 10 + (3 * stats.Luk);
        int perfectDodge = stats.Luk + 10;

        int maxHp = ScaleByPercent(build.HealthBase + ((long)build.HealthPerLevel * level), 100 + stats.Vit);
        int maxSp = ScaleByPercent(build.SpiritBase + ((long)build.SpiritPerLevel * level), 100 + stats.Int);

        return new DerivedStats(
            maxHp,
            maxSp,
            physicalAttack,
            magicalAttack,
            softDefense,
            softMagicDefense,
            hit,
            flee,
            critical,
            perfectDodge,
            CalculateAttackSpeed(stats, build.AttackSpeedPenalty),
            build.BaseMovementSpeed,
            CalculateVariableCastPermille(stats),
            FullCastPermille);
    }

    private static int ScaleByPercent(long value, int percent)
    {
        return (int)Math.Min(int.MaxValue, value * percent / 100);
    }

    // floor(sqrt(dex^2 / 5 + agi^2 / 2) / 4) equals the integer square root of (2 dex^2 + 5 agi^2) / 160.
    private static int CalculateAttackSpeed(PrimaryStats stats, int penalty)
    {
        long weighted = (2L * stats.Dex * stats.Dex) + (5L * stats.Agi * stats.Agi);
        int attackSpeed = AttackSpeedBase + (int)IntegerSquareRoot(weighted / 160) - penalty;
        return Math.Max(0, Math.Min(MaxAttackSpeed, attackSpeed));
    }

    // floor(1000 * (1 - sqrt(s / 530))) equals 1000 - ceil(sqrt(1000000 s / 530)).
    private static int CalculateVariableCastPermille(PrimaryStats stats)
    {
        long scaled = 1_000_000L * ((2L * stats.Dex) + stats.Int);
        long reduction = IntegerSquareRoot(scaled / VariableCastStatScale);
        if (reduction * reduction * VariableCastStatScale < scaled)
        {
            reduction++;
        }

        return (int)Math.Max(0, FullCastPermille - reduction);
    }

    private static long IntegerSquareRoot(long value)
    {
        long root = (long)Math.Sqrt(value);
        while (root * root > value)
        {
            root--;
        }

        while ((root + 1) * (root + 1) <= value)
        {
            root++;
        }

        return root;
    }
}
}
