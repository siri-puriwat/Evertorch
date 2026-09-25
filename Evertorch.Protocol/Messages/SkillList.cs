using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The skills the receiver's own character knows, sent to its owner alone after the inventory in every baseline
///     and again whenever one of its casts resolves (Network Protocol §9).
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
                || range < 0f
                || remainingCooldownMs > cooldownMs
                || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill)
                || Contains(skills, index, skill))
            {
                return false;
            }

            skills[index] = new SkillListEntry(skill, range, spCost, cooldownMs, afterCastDelayMs, remainingCooldownMs);
        }

        if (!reader.IsAtEnd)
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
                + 4 * sizeof(uint);
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
        }

        return writer.Position;
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
