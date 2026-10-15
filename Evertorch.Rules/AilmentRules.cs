using System;

namespace Evertorch.Rules
{
/// <summary>
///     How an ailment lands, lasts, and drains (Gameplay Systems §9.1; status ailments research note): the level
///     advantage of its source, a target's resistance, its chance in basis points, its duration, and poison's loss and
///     tick. It never draws: the server draws one <c>Next(10_000)</c> for each source and lands the ailment when the draw
///     is below the chance reckoned here.
/// </summary>
public sealed class AilmentRules
{
    /// <summary>A certain chance, in basis points.</summary>
    public const int CertainBasisPoints = 10_000;

    /// <summary>The shortest an ailment lasts, however well it is resisted.</summary>
    public const int MinDurationMs = 1000;

    private const int MaxResistance = 100;

    /// <summary>⌊max(0, Ls − Lt)² ÷ 5⌋: how far a higher-level source cuts its target's resistance.</summary>
    public int LevelAdvantage(int sourceLevel, int targetLevel)
    {
        long gap = Math.Max(0, sourceLevel - targetLevel);
        return (int)Math.Min(int.MaxValue, gap * gap / 5);
    }

    /// <summary>
    ///     A player's resistance, clamp(stat − A, 0, 100): its effective VIT, INT, or AGI, or its soft magic defense for
    ///     freeze.
    /// </summary>
    public int PlayerResistance(int statistic, int advantage)
    {
        return Clamped(statistic - (long)advantage);
    }

    /// <summary>A monster's resistance, clamp(level − A, 0, 100): its level alone.</summary>
    public int MonsterResistance(int level, int advantage)
    {
        return Clamped(level - (long)advantage);
    }

    /// <summary>A player's worn armor cards' resistance to one ailment, their sum capped at 100.</summary>
    public int CardResistance(int sum)
    {
        return Clamped(sum);
    }

    /// <summary>
    ///     The chance an ailment lands, in basis points: b = min(10,000, chance × scale), then b × (100 − R) × (100 − C)
    ///     ÷ 10,000, rounded down.
    /// </summary>
    public int LandingChance(int chanceBasisPoints, int chanceScale, int resistance, int cardResistance)
    {
        if (chanceBasisPoints < 0 || chanceScale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chanceBasisPoints),
                "A chance and its scale cannot be negative.");
        }

        long scaled = Math.Min(CertainBasisPoints, (long)chanceBasisPoints * chanceScale);
        return (int)Resisted(scaled, resistance, cardResistance);
    }

    /// <summary>
    ///     How long a landed ailment lasts: max(1,000, ⌊d × (100 − R) × (100 − C) ÷ 10,000⌋) ms.
    /// </summary>
    public int Duration(int durationMs, int resistance, int cardResistance)
    {
        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs, "A duration cannot be negative.");
        }

        return (int)Math.Max(MinDurationMs, Resisted(durationMs, resistance, cardResistance));
    }

    /// <summary>
    ///     What an armor card's and a statistic's resistance together keep out against a same-level source, the Stats
    ///     window's row: ⌊100 − (100 − R) × (100 − C) ÷ 100⌋.
    /// </summary>
    public int SheetResistance(int resistance, int cardResistance)
    {
        long kept = (long)(MaxResistance - Clamped(resistance)) * (MaxResistance - Clamped(cardResistance));
        return (int)((MaxResistance * MaxResistance - kept) / MaxResistance);
    }

    /// <summary>
    ///     Poison's loss each second: 2 + ⌊max HP × 15 ÷ 1,000⌋ for a player and × 5 for a monster.
    /// </summary>
    public int PoisonLoss(int maxHp, bool isPlayer)
    {
        if (maxHp < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "A body has HP.");
        }

        return (int)Math.Min(int.MaxValue, 2 + (long)maxHp * (isPlayer ? 15 : 5) / 1000);
    }

    /// <summary>
    ///     Whether a poison tick fires: only while HP is above a quarter of the maximum and above the loss, so poison
    ///     never kills.
    /// </summary>
    public bool DoesPoisonTick(int hp, int maxHp, int loss)
    {
        return hp > Math.Max(maxHp / 4, loss);
    }

    private static long Resisted(long value, int resistance, int cardResistance)
    {
        return value * (MaxResistance - Clamped(resistance)) * (MaxResistance - Clamped(cardResistance))
            / (MaxResistance * MaxResistance);
    }

    private static int Clamped(long value)
    {
        return (int)Math.Max(0, Math.Min(MaxResistance, value));
    }
}
}
