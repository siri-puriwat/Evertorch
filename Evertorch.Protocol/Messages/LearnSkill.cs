using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to learn one level of a skill of the character's job tree with a skill point (Gameplay Systems §9). An
///     accepted one answers with <see cref="SkillList" /> and <see cref="CharacterSheet" />.
/// </summary>
public sealed class LearnSkill
{
    public LearnSkill(SkillDefinitionId skill, uint commandSequence)
    {
        if (skill == default)
        {
            throw new ArgumentException("A skill is required.", nameof(skill));
        }

        Skill = skill;
        CommandSequence = commandSequence;
    }

    public SkillDefinitionId Skill { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a string that is not a skill ID.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out LearnSkill? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.LearnSkill)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string skillText)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill))
        {
            return false;
        }

        message = new LearnSkill(skill, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + WireText.GetEncodedLength(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.LearnSkill);
        writer.WriteString(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
