using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A cast began, to every client that knows the caster (Network Protocol §9). The cast time lets a client draw a
///     cast bar and hold its own caster still for as long as the server does.
/// </summary>
public sealed class SkillCastStarted
{
    public SkillCastStarted(EntityId caster, SkillDefinitionId skill, EntityId target, uint startTick, uint castMs)
    {
        if (skill == default)
        {
            throw new ArgumentException("A skill is required.", nameof(skill));
        }

        Caster = caster;
        Skill = skill;
        Target = target;
        StartTick = startTick;
        CastMs = castMs;
    }

    public EntityId Caster { get; }

    public SkillDefinitionId Skill { get; }

    /// <summary>
    ///     0 when the caster casts on itself or the receiver has no spawn for the target.
    /// </summary>
    public EntityId Target { get; }

    public uint StartTick { get; }

    /// <summary>
    ///     Milliseconds until the cast resolves; 0 resolves in the tick it began.
    /// </summary>
    public uint CastMs { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out SkillCastStarted? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.SkillCastStarted)
            || !reader.TryReadInt64(out long caster)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string skillText)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadUInt32(out uint startTick)
            || !reader.TryReadUInt32(out uint castMs)
            || !reader.IsAtEnd
            || caster == 0
            || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill))
        {
            return false;
        }

        message = new SkillCastStarted(new EntityId(caster), skill, new EntityId(target), startTick, castMs);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(long)
            + 2 * sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.SkillCastStarted);
        writer.WriteInt64(Caster.Value);
        writer.WriteString(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteInt64(Target.Value);
        writer.WriteUInt32(StartTick);
        writer.WriteUInt32(CastMs);
        return writer.Position;
    }
}
}
