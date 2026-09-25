using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to cast a skill at a target, 0 for the caster itself (Network Protocol §8). The server runs
///     every check and answers a refusal with its reason.
/// </summary>
public sealed class UseSkill
{
    public UseSkill(SkillDefinitionId skill, EntityId target, uint commandSequence)
    {
        if (skill == default)
        {
            throw new ArgumentException("A skill is required.", nameof(skill));
        }

        Skill = skill;
        Target = target;
        CommandSequence = commandSequence;
    }

    public SkillDefinitionId Skill { get; }

    public EntityId Target { get; }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out UseSkill? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.UseSkill)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string skillText)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill))
        {
            return false;
        }

        message = new UseSkill(skill, new EntityId(target), sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(long)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.UseSkill);
        writer.WriteString(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteInt64(Target.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
