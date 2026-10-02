using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     A skill (Gameplay Systems §9): how it targets, its range or the area it strikes, when its SP is paid, what each
///     of its levels does, and the skill it may require. A job's basic attack has no levels, since swings do not go
///     through skills.
/// </summary>
public sealed class SkillDefinition
{
    public SkillDefinition(
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        SkillDamageType? damageType,
        double range,
        SkillPaymentPoint spPaidAt,
        IReadOnlyList<SkillLevel> levels,
        SkillRequirement? requires = null,
        double areaRadius = 0d)
    {
        Id = id;
        DisplayName = displayName;
        TargetType = targetType;
        DamageType = damageType;
        Range = range;
        SpPaidAt = spPaidAt;
        Levels = levels ?? throw new ArgumentNullException(nameof(levels));
        Requires = requires;
        AreaRadius = areaRadius;
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    /// <summary>
    ///     Required for a job's basic attack and a damage effect; absent otherwise.
    /// </summary>
    public SkillDamageType? DamageType { get; }

    /// <summary>World units.</summary>
    public double Range { get; }

    /// <summary>
    ///     The radius around its caster within which a damage skill cast on itself strikes every player in sight
    ///     (Gameplay Systems §9); 0 for a skill with no area.
    /// </summary>
    public double AreaRadius { get; }

    public bool HasArea => AreaRadius > 0d;

    public SkillPaymentPoint SpPaidAt { get; }

    /// <summary>
    ///     Level 1 first; empty for a basic attack.
    /// </summary>
    public IReadOnlyList<SkillLevel> Levels { get; }

    public int MaxLevel => Levels.Count;

    /// <summary>
    ///     Whether the skill does something when it resolves; a basic attack does not.
    /// </summary>
    public bool HasEffect => Levels.Count > 0;

    public SkillRequirement? Requires { get; }

    /// <summary>
    ///     The values of <paramref name="level" />, held to the skill's levels.
    /// </summary>
    public SkillLevel ValuesAt(int level)
    {
        if (Levels.Count == 0)
        {
            throw new InvalidOperationException($"Skill {Id} has no levels.");
        }

        return Levels[Math.Min(Math.Max(level, 1), Levels.Count) - 1];
    }
}
}
