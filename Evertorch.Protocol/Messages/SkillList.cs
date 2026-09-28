using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's job tree, every skill at the level learned or at 0, sent to its owner alone after
///     the inventory in every baseline and again whenever one of its casts resolves or it learns a level (Network
///     Protocol §9).
/// </summary>
public sealed class SkillList
{
    /// <summary>
    ///     The most skills one list holds, so it fits one reliable message even with the longest IDs.
    /// </summary>
    public const int MaxEntries = 11;

    public SkillList(IReadOnlyList<SkillListEntry> skills)
    {
        if (skills == null)
        {
            throw new ArgumentNullException(nameof(skills));
        }

        if (skills.Count > MaxEntries)
        {
            throw new ArgumentException($"A skill list holds at most {MaxEntries} entries.", nameof(skills));
        }

        Skills = skills;
    }

    public IReadOnlyList<SkillListEntry> Skills { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out SkillList? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.SkillList)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntries)
        {
            return false;
        }

        var skills = new SkillListEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string skillText)
                || !reader.TryReadSingle(out float range)
                || !reader.TryReadUInt32(out uint spCost)
                || !reader.TryReadUInt32(out uint cooldownMs)
                || !reader.TryReadUInt32(out uint afterCastDelayMs)
                || !reader.TryReadUInt32(out uint remainingCooldownMs)
                || !reader.TryReadByte(out byte level)
                || !reader.TryReadByte(out byte maxLevel)
                || !reader.TryReadByte(out byte prerequisiteIndex)
                || !reader.TryReadByte(out byte prerequisiteLevel)
                || range < 0f
                || remainingCooldownMs > cooldownMs
                || maxLevel < 1
                || maxLevel > ContentLimits.MaxSkillLevel
                || level > maxLevel
                || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill)
                || Contains(skills, index, skill))
            {
                return false;
            }

            skills[index] = new SkillListEntry(
                skill,
                range,
                spCost,
                cooldownMs,
                afterCastDelayMs,
                remainingCooldownMs,
                level,
                maxLevel,
                prerequisiteIndex,
                prerequisiteLevel);
        }

        if (!reader.IsAtEnd || !HasValidPrerequisites(skills))
        {
            return false;
        }

        message = new SkillList(skills);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (SkillListEntry entry in Skills)
        {
            length += WireText.GetEncodedLength(entry.Skill.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(float)
                + 4 * sizeof(uint)
                + 4 * sizeof(byte);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.SkillList);
        writer.WriteByte((byte)Skills.Count);
        foreach (SkillListEntry entry in Skills)
        {
            writer.WriteString(entry.Skill.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteSingle(entry.Range);
            writer.WriteUInt32(entry.SpCost);
            writer.WriteUInt32(entry.CooldownMs);
            writer.WriteUInt32(entry.AfterCastDelayMs);
            writer.WriteUInt32(entry.RemainingCooldownMs);
            writer.WriteByte(entry.Level);
            writer.WriteByte(entry.MaxLevel);
            writer.WriteByte(entry.PrerequisiteIndex);
            writer.WriteByte(entry.PrerequisiteLevel);
        }

        return writer.Position;
    }

    // A prerequisite is another entry of the same list, needed at 1 to its maximum; none carries level 0.
    private static bool HasValidPrerequisites(SkillListEntry[] skills)
    {
        for (int index = 0; index < skills.Length; index++)
        {
            SkillListEntry entry = skills[index];
            if (entry.PrerequisiteIndex == SkillListEntry.NoPrerequisite)
            {
                if (entry.PrerequisiteLevel != 0)
                {
                    return false;
                }

                continue;
            }

            if (entry.PrerequisiteIndex >= skills.Length
                || entry.PrerequisiteIndex == index
                || entry.PrerequisiteLevel < 1
                || entry.PrerequisiteLevel > skills[entry.PrerequisiteIndex].MaxLevel)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(SkillListEntry[] skills, int count, SkillDefinitionId skill)
    {
        for (int index = 0; index < count; index++)
        {
            if (skills[index].Skill == skill)
            {
                return true;
            }
        }

        return false;
    }
}
}
