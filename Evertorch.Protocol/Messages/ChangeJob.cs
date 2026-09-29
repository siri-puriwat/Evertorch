using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the NPC that offers it to change the character's job to a first job of its job (Gameplay Systems §6.1).
///     It is committed at once, and an accepted one answers after the commit with <see cref="SkillList" /> and
///     <see cref="CharacterSheet" />, which names the new job; the players who see the character get its new body in a
///     second <c>EntitySpawn</c>.
/// </summary>
public sealed class ChangeJob
{
    public ChangeJob(EntityId npc, JobDefinitionId job, uint commandSequence)
    {
        if (npc.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npc), "An NPC entity is above 0.");
        }

        if (job == default)
        {
            throw new ArgumentException("A job is required.", nameof(job));
        }

        Npc = npc;
        Job = job;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    /// <summary>
    ///     The first job the character becomes.
    /// </summary>
    public JobDefinitionId Job { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an NPC entity of 0 or below and a string that is not a job ID.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ChangeJob? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ChangeJob)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job))
        {
            return false;
        }

        message = new ChangeJob(new EntityId(npc), job, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + sizeof(long) + WireText.GetEncodedLength(Job.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ChangeJob);
        writer.WriteInt64(Npc.Value);
        writer.WriteString(Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
