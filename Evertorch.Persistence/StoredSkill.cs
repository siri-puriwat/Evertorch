using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A skill a character has learned, at its level (Persistence §4). The skill's ID is the stored text, which the
///     current content may no longer define (Persistence §8).
/// </summary>
public sealed class StoredSkill
{
    public StoredSkill(string skillDefinitionId, int level)
    {
        SkillDefinitionId = skillDefinitionId ?? throw new ArgumentNullException(nameof(skillDefinitionId));
        Level = level;
    }

    public string SkillDefinitionId { get; }

    public int Level { get; }
}
}
