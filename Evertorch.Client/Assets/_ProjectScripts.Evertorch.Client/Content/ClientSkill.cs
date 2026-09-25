using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The client-safe part of a skill definition: its name, whether it is used on an enemy or on the caster, and the
///     logical key of its icon.
/// </summary>
public sealed class ClientSkill
{
    public ClientSkill(SkillDefinitionId id, string displayName, SkillTargetType targetType, string iconKey)
    {
        Id = id;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        TargetType = targetType;
        IconKey = iconKey ?? throw new ArgumentNullException(nameof(iconKey));
    }

    public SkillDefinitionId Id { get; }

    public string DisplayName { get; }

    public SkillTargetType TargetType { get; }

    public string IconKey { get; }
}
}
