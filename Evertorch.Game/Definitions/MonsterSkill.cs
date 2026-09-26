namespace Evertorch.Game
{
/// <summary>
///     A skill a monster may cast, and the chance it is tried at a decision (Gameplay Systems §10).
/// </summary>
public sealed class MonsterSkill
{
    public MonsterSkill(SkillDefinitionId skill, double chance)
    {
        Skill = skill;
        Chance = chance;
    }

    public SkillDefinitionId Skill { get; }

    /// <summary>
    ///     Probability from 0 to 1 inclusive that the monster casts the skill at a decision where it could.
    /// </summary>
    public double Chance { get; }
}
}
