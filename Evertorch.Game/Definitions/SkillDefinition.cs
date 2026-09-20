namespace Evertorch.Game
{
public sealed class SkillDefinition
{
    public SkillDefinition(
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        SkillDamageType damageType,
        double range)
    {
        Id = id;
        DisplayName = displayName;
        TargetType = targetType;
        DamageType = damageType;
        Range = range;
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    public SkillDamageType DamageType { get; }

    /// <summary>World units.</summary>
    public double Range { get; }
}
}
