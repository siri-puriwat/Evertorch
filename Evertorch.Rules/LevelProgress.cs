using System;

namespace Evertorch.Rules
{
/// <summary>
///     A character's base level and the experience it holds toward the next one.
/// </summary>
public readonly struct LevelProgress
{
    public LevelProgress(int level, long experience)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Base level starts at 1.");
        }

        if (experience < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(experience), "Experience cannot be negative.");
        }

        Level = level;
        Experience = experience;
    }

    public int Level { get; }

    public long Experience { get; }
}
}
