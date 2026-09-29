using Evertorch.Game;

namespace Evertorch.Rules
{
public interface IProgressionRules
{
    /// <summary>
    ///     The highest value a primary statistic reaches by spending stat points.
    /// </summary>
    int StatCap { get; }

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

    /// <summary>
    ///     The stat points reaching base level <paramref name="level" /> grants; none for level 1.
    /// </summary>
    int StatPointsForLevel(int level);

    /// <summary>
    ///     The stat points every base level up to <paramref name="level" /> has granted in all.
    /// </summary>
    int StatPointsGranted(int level);

    /// <summary>
    ///     The stat points raising a primary statistic from <paramref name="value" /> to the next value costs.
    /// </summary>
    int StatRaiseCost(int value);

    /// <summary>
    ///     The skill points every job level up to <paramref name="jobLevel" /> has granted in all: one for each job
    ///     level from 2, beside the <paramref name="carriedSkillPoints" /> a first job is granted for its base job's
    ///     levels (Gameplay Systems §2.1).
    /// </summary>
    int SkillPointsGranted(int jobLevel, int carriedSkillPoints);
}
}
