namespace Evertorch.Game
{
public sealed class JobDefinition
{
    public JobDefinition(
        JobDefinitionId id,
        string displayName,
        PrimaryStats startingStats,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        int unarmedAttackSpeedPenalty,
        double baseSpeed,
        MapDefinitionId startingMap,
        SkillDefinitionId basicAttack)
    {
        Id = id;
        DisplayName = displayName;
        StartingStats = startingStats;
        HealthBase = healthBase;
        HealthPerLevel = healthPerLevel;
        SpiritBase = spiritBase;
        SpiritPerLevel = spiritPerLevel;
        UnarmedAttackSpeedPenalty = unarmedAttackSpeedPenalty;
        BaseSpeed = baseSpeed;
        StartingMap = startingMap;
        BasicAttack = basicAttack;
    }

    public JobDefinitionId Id { get; }

    public string DisplayName { get; }

    public PrimaryStats StartingStats { get; }

    public int HealthBase { get; }

    public int HealthPerLevel { get; }

    public int SpiritBase { get; }

    public int SpiritPerLevel { get; }

    public int UnarmedAttackSpeedPenalty { get; }

    /// <summary>World units per second before any movement rule is applied.</summary>
    public double BaseSpeed { get; }

    public MapDefinitionId StartingMap { get; }

    public SkillDefinitionId BasicAttack { get; }
}
}
