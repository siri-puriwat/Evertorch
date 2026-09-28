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

    public int StatPointsGranted(int level)
    {
        int granted = 0;
        for (int reached = 2; reached <= level; reached++)
        {
            granted += StatPointsForLevel(reached);
        }

        return granted;
    }

    public int StatRaiseCost(int value)
    {
        return 2 + Math.Max(0, value - 1) / 10;
    }

    public int SkillPointsGranted(int jobLevel)
    {
        return Math.Max(0, jobLevel - 1);
    }
}
}
