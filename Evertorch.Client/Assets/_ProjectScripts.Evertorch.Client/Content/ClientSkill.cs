using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a skill definition: its name, whether it is used on an enemy or on the caster, the
///     logical key of its icon, and its description for the player (Content Pipeline §5).
/// </summary>
public sealed class ClientSkill
{
    public ClientSkill(
        SkillDefinitionId id,
        string displayName,
        SkillTargetType targetType,
        string iconKey,
        string description = "")
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        TargetType = targetType;
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    public string IconKey { get; }

    /// <summary>
    ///     What the Skills window says the skill does; empty when the content writes none.
    /// </summary>
    public string Description { get; }
}
}
