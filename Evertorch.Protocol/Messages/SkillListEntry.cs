using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One skill the character knows, with the numbers its client needs to use it (Network Protocol §9).
/// </summary>
public readonly struct SkillListEntry
{
    public SkillListEntry(
        SkillDefinitionId skill,
        float range,
        uint spCost,
        uint cooldownMs,
        uint afterCastDelayMs,
        uint remainingCooldownMs)
    {
        Skill = skill;
        Range = range;
        SpCost = spCost;
        CooldownMs = cooldownMs;
        AfterCastDelayMs = afterCastDelayMs;
        RemainingCooldownMs = remainingCooldownMs;
    }

    public SkillDefinitionId Skill { get; }

    /// <summary>
    ///     World units; an enemy skill's client walks within it before asking.
    /// </summary>
    public float Range { get; }

    public uint SpCost { get; }

    public uint CooldownMs { get; }

    public uint AfterCastDelayMs { get; }

    /// <summary>
    ///     What was left of the cooldown when the server sent the list; never more than <see cref="CooldownMs" />.
    /// </summary>
    public uint RemainingCooldownMs { get; }
}
}
