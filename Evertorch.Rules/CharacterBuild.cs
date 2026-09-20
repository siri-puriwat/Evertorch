using System;
using Evertorch.Game;

namespace Evertorch.Rules
{
/// <summary>
/// Base inputs for derived statistics: level, primary stats, and the tuning values of the job.
/// </summary>
public readonly struct CharacterBuild
{
    public CharacterBuild(
        int baseLevel,
        PrimaryStats stats,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        int attackSpeedPenalty,
        float baseMovementSpeed)
    {
        if (baseLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseLevel), "Base level starts at 1.");
        }

        if (healthBase < 0 || healthPerLevel < 0 || spiritBase < 0 || spiritPerLevel < 0 || attackSpeedPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(healthBase), "Job tuning values cannot be negative.");
        }

        BaseLevel = baseLevel;
        Stats = stats;
        HealthBase = healthBase;
        HealthPerLevel = healthPerLevel;
        SpiritBase = spiritBase;
        SpiritPerLevel = spiritPerLevel;
        AttackSpeedPenalty = attackSpeedPenalty;
        BaseMovementSpeed = baseMovementSpeed;
    }

    public int BaseLevel { get; }

    public PrimaryStats Stats { get; }

    public int HealthBase { get; }

    public int HealthPerLevel { get; }

    public int SpiritBase { get; }

    public int SpiritPerLevel { get; }

    /// <summary>Attack speed lost to the equipped weapon type; the unarmed value comes from the job.</summary>
    public int AttackSpeedPenalty { get; }

    public float BaseMovementSpeed { get; }
}
}
