using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     Experience shares and levelling as the experience research note describes them (Gameplay Systems §2.1), and the
///     stat and skill points of the Adventurer build (Gameplay Systems §2, §9).
/// </summary>
public sealed class RenewalProgressionRules : IProgressionRules
{
    public int StatCap => ContentLimits.MaxPrimaryStat;

    public long ShareExperience(long baseExperience, long damage, long totalDamage)
    {
        if (baseExperience <= 0 || damage <= 0 || totalDamage <= 0)
        {
            return 0;
        }

        // The product can pass 64 bits before the division brings it back; the share itself never does.
        long share = (long)((Int128)baseExperience * damage / totalDamage);
        return Math.Max(1, share);
    }

    public long SharePartyExperience(long pool, int count)
    {
        return pool <= 0 || count <= 0 ? 0 : pool / count;
    }

    public LevelProgress AddExperience(ExperienceTableDefinition table, LevelProgress current, long experience)
    {
        int cap = table.Levels.Count + 1;
        int level = current.Level;
        if (level >= cap)
        {
            return new LevelProgress(level, 0);
        }

        long held = current.Experience + Math.Max(0, experience);
        while (level < cap && held >= table.Levels[level - 1])
        {
            held -= table.Levels[level - 1];
            level++;
        }

        // The cap drops the surplus.
        return new LevelProgress(level, level >= cap ? 0 : held);
    }

    public long ExperienceToNextLevel(ExperienceTableDefinition table, int level)
    {
        return level >= 1 && level <= table.Levels.Count ? table.Levels[level - 1] : 0;
    }

    public int StatPointsForLevel(int level)
    {
        return level < 2 ? 0 : 3 + level / 5;
    }

    // The sum of 3 + floor(L / 5) for L from 2 to the level, in closed form: a stored level no content reaches must not
    // loop the tick thread, and the sum is held to what an int carries.
    public int StatPointsGranted(int level)
    {
        if (level < 2)
        {
            return 0;
        }

        long whole = level / 5;
        long floors = 5 * whole * (whole - 1) / 2 + whole * (level % 5 + 1);
        long granted = 3L * (level - 1) + floors;
        return (int)Math.Min(int.MaxValue, granted);
    }

    public int StatRaiseCost(int value)
    {
        return 2 + Math.Max(0, value - 1) / 10;
    }

    public int SkillPointsGranted(int jobLevel, int carriedSkillPoints)
    {
        return Math.Max(0, carriedSkillPoints) + Math.Max(0, jobLevel - 1);
    }
}
}
