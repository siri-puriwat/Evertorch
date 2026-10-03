namespace Evertorch.Game
{
/// <summary>
///     Numeric bounds for content, which reject nonsense and leave tuning to design. The tools check canonical content
///     against them, and the server's loader checks its package against the same bounds, so a hand-edited package
///     cannot carry a value the tools refuse (Content Pipeline §7).
/// </summary>
public static class ContentLimits
{
    public const int MaxLevel = 999;

    /// <summary>
    ///     A table of n levels caps its jobs at level n + 1, which must stay within <see cref="MaxLevel" />.
    /// </summary>
    public const int MaxExperienceLevels = MaxLevel - 1;

    public const int MaxStat = 9999;

    /// <summary>
    ///     The cap of a stored primary statistic (Gameplay Systems §2): stat points raise one no further, and a job starts
    ///     within it.
    /// </summary>
    public const int MaxPrimaryStat = 99;

    /// <summary>
    ///     The highest level a skill can have (Gameplay Systems §9).
    /// </summary>
    public const int MaxSkillLevel = 5;

    /// <summary>
    ///     A status level's percentage of a statistic (Gameplay Systems §9.1): a debuff takes away at most 99 %.
    /// </summary>
    public const int MinStatPercent = -99;

    public const int MaxStatPercent = 1000;
    public const int MaxAttackSpeedPenalty = 200;
    public const int MaxDamageRatioPercent = 10_000;
    public const int MaxHp = 100_000_000;
    public const int MaxStack = 1_000_000;
    public const int MaxPrice = 1_000_000_000;

    /// <summary>
    ///     The most a Storekeeper takes for one deposit (Content Pipeline §4).
    /// </summary>
    public const int MaxDepositFee = 1_000_000;

    /// <summary>
    ///     The coin cap (Gameplay Systems §11.3).
    /// </summary>
    public const int MaxCurrency = 1_000_000_000;

    public const int MaxKillCount = 1000;
    public const int MaxExperience = 1_000_000_000;
    public const int MaxDurationMs = 86_400_000;

    /// <summary>
    ///     The soonest a dead monster returns, whatever its spawn's respawn (Gameplay Systems §10).
    /// </summary>
    public const int MinRespawnMs = 1000;

    public const int MaxSpawnCount = 1000;
    public const double MaxDistance = 10_000d;
    public const double MaxCoordinate = 100_000d;
    public const double MaxSpeed = 100d;
    public const double MaxCellSize = 100d;
    public const double MaxAgentRadius = 10d;
    public const double MaxStepHeight = 100d;

    /// <summary>
    ///     The smallest and the largest a monster's body is drawn, as a multiple of its model's own size (Prototype
    ///     Content §2).
    /// </summary>
    public const double MinBodyScale = 0.25d;

    public const double MaxBodyScale = 4d;

    /// <summary>
    ///     Generated packages name each distinct cell kind with one letter.
    /// </summary>
    public const int MaxLegendEntries = 52;
}
}
