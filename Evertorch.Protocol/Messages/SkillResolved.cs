using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A cast resolved on its target, to every client that knows the target (Network Protocol §9). A heal carries its
///     nominal amount, so no other player's HP leaks.
/// </summary>
public sealed class SkillResolved
{
    public SkillResolved(
        EntityId caster,
        EntityId target,
        SkillDefinitionId skill,
        SkillOutcome outcome,
        uint amount,
        uint serverTick,
        ushort targetHealthPermille)
    {
        if (skill == default)
        {
            throw new ArgumentException("A skill is required.", nameof(skill));
        }

        Caster = caster;
        Target = target;
        Skill = skill;
        Outcome = outcome;
        Amount = amount;
        ServerTick = serverTick;
        TargetHealthPermille = targetHealthPermille;
    }

    /// <summary>
    ///     0 when the receiver has no spawn for the caster.
    /// </summary>
    public EntityId Caster { get; }

    public EntityId Target { get; }

    public SkillDefinitionId Skill { get; }

    public SkillOutcome Outcome { get; }

    /// <summary>
    ///     The damage dealt, 0 for a miss, or the heal's nominal amount.
    /// </summary>
    public uint Amount { get; }

    public uint ServerTick { get; }

    /// <summary>
    ///     A monster target's HP ratio after the cast, for its health bar; 0 for other kinds.
    /// </summary>
    public ushort TargetHealthPermille { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out SkillResolved? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.SkillResolved)
            || !reader.TryReadInt64(out long caster)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string skillText)
            || !reader.TryReadByte(out byte outcome)
            || !reader.TryReadUInt32(out uint amount)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.TryReadUInt16(out ushort permille)
            || !reader.IsAtEnd
            || target == 0
            || !WireEnums.IsDefined((SkillOutcome)outcome)
            || permille > HealthRatio.Full
            || !SkillDefinitionId.TryCreate(skillText, out SkillDefinitionId skill))
        {
            return false;
        }

        message = new SkillResolved(
            new EntityId(caster),
            new EntityId(target),
            skill,
            (SkillOutcome)outcome,
            amount,
            serverTick,
            permille);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + 2 * sizeof(long)
            + WireText.GetEncodedLength(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(byte)
            + 2 * sizeof(uint)
            + sizeof(ushort);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.SkillResolved);
        writer.WriteInt64(Caster.Value);
        writer.WriteInt64(Target.Value);
        writer.WriteString(Skill.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteByte((byte)Outcome);
        writer.WriteUInt32(Amount);
        writer.WriteUInt32(ServerTick);
        writer.WriteUInt16(TargetHealthPermille);
        return writer.Position;
    }
}
}
