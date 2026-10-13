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

    /// <summary>
    ///     The most levels a refine table lists, so an item refines to +10 at most (Gameplay Systems §11.6).
    /// </summary>
    public const int MaxRefineLevels = 10;

    /// <summary>
    ///     A refine step's chance, in percent: certain at 100.
    /// </summary>
    public const int MaxRefineChance = 100;

    public const int MaxRefineCoins = 1_000_000;
    public const int MaxRefineBonus = 9_999;

    /// <summary>
    ///     The most levels a failure lowers an item by.
    /// </summary>
    public const int MaxRefineDowngrade = 9;

    /// <summary>
    ///     The most ore one attempt takes, what the wire's byte carries.
    /// </summary>
    public const int MaxRefineMaterialQuantity = 255;

    /// <summary>
    ///     The most items one refine table refines, the room of its message (Network Protocol §6).
    /// </summary>
    public const int MaxRefineItems = 13;

    /// <summary>
    ///     The most cards a weapon holds, and an armor (Gameplay Systems §11.7).
    /// </summary>
    public const int MaxWeaponSlots = 4;

    public const int MaxArmorSlots = 1;

    /// <summary>
    ///     The most cards content holds, the room of one list of removal fees (Network Protocol §6).
    /// </summary>
    public const int MaxCards = 14;

    public const int MaxBossDamagePercent = 100;

    /// <summary>
    ///     The most a card takes off a boss's damage, in percent; combat caps the worn cards' sum there too.
    /// </summary>
    public const int MaxBossDamageReductionPercent = 90;

    public const int MaxCardRemovalFee = 1_000_000;

    /// <summary>
    ///     The most a card adds to its wearer's damage to one race, element, or size, in percent (Gameplay Systems
    ///     §11.1).
    /// </summary>
    public const int MaxCardDamagePercent = 100;

    /// <summary>
    ///     The most a card takes off the damage of one element, in percent; combat caps the worn cards' sum there too.
    /// </summary>
    public const int MaxCardDamageReductionPercent = 90;

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
