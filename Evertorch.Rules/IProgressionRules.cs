using Evertorch.Game;

namespace Evertorch.Rules
{
public interface IProgressionRules
{
    /// <summary>
    ///     A character's share of a dead monster's <paramref name="baseExperience" /> for <paramref name="damage" /> of
    ///     the <paramref name="totalDamage" /> the monster logged.
    /// </summary>
    long ShareExperience(long baseExperience, long damage, long totalDamage);

    /// <summary>
    ///     <paramref name="current" /> after gaining <paramref name="experience" />, carried through as many levels as
    ///     it covers up to the cap of <paramref name="table" />.
    /// </summary>
    LevelProgress AddExperience(ExperienceTableDefinition table, LevelProgress current, long experience);

    /// <summary>
    ///     The experience <paramref name="level" /> needs to reach the next level; 0 at or above the cap.
    /// </summary>
    long ExperienceToNextLevel(ExperienceTableDefinition table, int level);
}
}
