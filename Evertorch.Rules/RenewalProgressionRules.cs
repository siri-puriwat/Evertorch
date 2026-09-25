using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
///     Experience shares and levelling as the experience research note describes them (Gameplay Systems §2.1).
/// </summary>
public sealed class RenewalProgressionRules : IProgressionRules
{
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
}
}
