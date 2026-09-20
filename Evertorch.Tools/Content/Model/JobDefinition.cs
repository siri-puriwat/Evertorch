using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class JobDefinition
{
    public JobDefinition(
        DefinitionSource source,
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
        SkillDefinitionId basicAttack,
        string prefab)
    {
        Source = source;
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
        Prefab = prefab;
    }

    public DefinitionSource Source { get; }

    public JobDefinitionId Id { get; }

    public string DisplayName { get; }

    public PrimaryStats StartingStats { get; }

    public int HealthBase { get; }

    public int HealthPerLevel { get; }

    public int SpiritBase { get; }

    public int SpiritPerLevel { get; }

    public int UnarmedAttackSpeedPenalty { get; }

    public double BaseSpeed { get; }

    public MapDefinitionId StartingMap { get; }

    public SkillDefinitionId BasicAttack { get; }

    public string Prefab { get; }
}
}
