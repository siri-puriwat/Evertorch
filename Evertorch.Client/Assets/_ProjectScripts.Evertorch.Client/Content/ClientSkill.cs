using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a skill definition: its name, whether it is used on an enemy, on the caster, or on an
///     ally, the logical keys of its icon and of what it flies, and its description for the player (Content Pipeline
///     §5).
/// </summary>
public sealed class ClientSkill
{
    public ClientSkill(
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        string iconKey,
        string description = "",
        string projectileKey = "",
        float areaRadius = 0f)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        TargetType = targetType;
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        ProjectileKey = projectileKey ?? throw new ArgumentNullException(nameof(projectileKey));
        AreaRadius = areaRadius;
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    public string IconKey { get; }

    /// <summary>
    ///     What the Skills window says the skill does; empty when the content writes none.
    /// </summary>
    public string Description { get; }

    /// <summary>
    ///     What the skill flies from its caster to its target; empty when it flies nothing.
    /// </summary>
    public string ProjectileKey { get; }

    /// <summary>
    ///     The radius around its caster that a skill cast on itself strikes, which the player sees as its telegraph
    ///     (Gameplay Systems §9); 0 for a skill with no area.
    /// </summary>
    public float AreaRadius { get; }
}
}
