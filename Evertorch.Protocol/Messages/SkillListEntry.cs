using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One skill of the character's job tree, at level 0 while not learned, with the numbers its client needs to use it
///     and to show what learning it takes (Network Protocol §9).
/// </summary>
public readonly struct SkillListEntry
{
    /// <summary>
    ///     The <see cref="PrerequisiteIndex" /> of a skill that requires none.
    /// </summary>
    public const byte NoPrerequisite = byte.MaxValue;

    public SkillListEntry(
        SkillDefinitionId skill,
        float range,
        uint spCost,
        uint cooldownMs,
        uint afterCastDelayMs,
        uint remainingCooldownMs,
        byte level,
        byte maxLevel,
        byte prerequisiteIndex,
        byte prerequisiteLevel)
    {
        Skill = skill;
        Range = range;
        SpCost = spCost;
        CooldownMs = cooldownMs;
        AfterCastDelayMs = afterCastDelayMs;
        RemainingCooldownMs = remainingCooldownMs;
        Level = level;
        MaxLevel = maxLevel;
        PrerequisiteIndex = prerequisiteIndex;
        PrerequisiteLevel = prerequisiteLevel;
    }

    public SkillDefinitionId Skill { get; }

    /// <summary>
    ///     World units; an enemy skill's client walks within it before asking.
    /// </summary>
    public float Range { get; }

    /// <summary>
    ///     This and the timings are the learned level's values, or level 1's while none is learned.
    /// </summary>
    public uint SpCost { get; }

    public uint CooldownMs { get; }

    public uint AfterCastDelayMs { get; }

    /// <summary>
    ///     What was left of the cooldown when the server sent the list; never more than <see cref="CooldownMs" />.
    /// </summary>
    public uint RemainingCooldownMs { get; }

    /// <summary>
    ///     The level learned; 0 while the skill is not learned and cannot be used.
    /// </summary>
    public byte Level { get; }

    public byte MaxLevel { get; }

    /// <summary>
    ///     The index in the same list of the skill this one requires, or <see cref="NoPrerequisite" />.
    /// </summary>
    public byte PrerequisiteIndex { get; }

    /// <summary>
    ///     The level of the required skill this one needs; 0 without a prerequisite.
    /// </summary>
    public byte PrerequisiteLevel { get; }

    public bool IsLearned => Level > 0;
}
}
