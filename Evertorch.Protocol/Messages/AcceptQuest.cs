using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to accept a quest the NPC gives (Gameplay Systems §2.2, §6.1). The server runs every check and answers with
///     <see cref="QuestLog" />, or with <see cref="CommandRejected" />.
/// </summary>
public sealed class AcceptQuest
{
    public AcceptQuest(EntityId npc, QuestDefinitionId quest, uint commandSequence)
    {
        if (quest == default)
        {
            throw new ArgumentException("A quest is required.", nameof(quest));
        }

        Npc = npc;
        Quest = quest;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public QuestDefinitionId Quest { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message: an NPC entity of 0 or below, or a string that is not a quest ID.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out AcceptQuest? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.AcceptQuest)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string questText)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || !QuestDefinitionId.TryCreate(questText, out QuestDefinitionId quest))
        {
            return false;
        }

        message = new AcceptQuest(new EntityId(npc), quest, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(Quest.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.AcceptQuest);
        writer.WriteInt64(Npc.Value);
        writer.WriteString(Quest.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
