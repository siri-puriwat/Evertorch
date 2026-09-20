using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class SkillDefinition
{
    public SkillDefinition(
        DefinitionSource source,
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        SkillDamageType damageType,
        double range,
        string icon)
    {
        Source = source;
        Id = id;
        DisplayName = displayName;
        TargetType = targetType;
        DamageType = damageType;
        Range = range;
        Icon = icon;
    }

    public DefinitionSource Source { get; }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    public SkillDamageType DamageType { get; }

    public double Range { get; }

    public string Icon { get; }
}
}
