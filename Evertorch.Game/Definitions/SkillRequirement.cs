namespace Evertorch.Game
{
/// <summary>
///     The level of another skill of the same tree that learning a skill requires (Gameplay Systems §9).
/// </summary>
public sealed class SkillRequirement
{
    public SkillRequirement(SkillDefinitionId skill, int level)
    {
        Skill = skill;
        Level = level;
    }

    public SkillDefinitionId Skill { get; }

    public int Level { get; }
}
}
