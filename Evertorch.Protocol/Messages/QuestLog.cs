using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own quests, active and completed, ordered by quest ID, sent to its owner alone after the status
///     effects in every baseline and again whenever one is accepted, advances, or is completed (Network Protocol §9).
/// </summary>
public sealed class QuestLog
{
    /// <summary>
    ///     The most quests one message holds, so it fits one reliable message even with the longest IDs.
    /// </summary>
    public const int MaxEntries = 14;

    public QuestLog(IReadOnlyList<QuestLogEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException($"A quest log holds at most {MaxEntries} entries.", nameof(entries));
        }

        Entries = entries;
    }

    public IReadOnlyList<QuestLogEntry> Entries { get; }

    /// <summary>
    ///     False for a malformed message: more than <see cref="MaxEntries" /> entries, a string that is not a quest ID,
    ///     the same quest twice, an unknown state, a count of 0, progress past the count, or a completed quest short of
    ///     it.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out QuestLog? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.QuestLog)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new QuestLogEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string questText)
                || !reader.TryReadByte(out byte stateValue)
                || !reader.TryReadUInt16(out ushort progress)
                || !reader.TryReadUInt16(out ushort needed)
                || !QuestDefinitionId.TryCreate(questText, out QuestDefinitionId quest)
                || !WireEnums.IsDefined((QuestState)stateValue)
                || needed == 0
                || progress > needed
                || ((QuestState)stateValue == QuestState.Completed && progress != needed)
                || Contains(entries, index, quest))
            {
                return false;
            }

            entries[index] = new QuestLogEntry(quest, (QuestState)stateValue, progress, needed);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new QuestLog(entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (QuestLogEntry entry in Entries)
        {
            length += WireText.GetEncodedLength(entry.Quest.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(byte)
                + 2 * sizeof(ushort);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.QuestLog);
        writer.WriteByte((byte)Entries.Count);
        foreach (QuestLogEntry entry in Entries)
        {
            writer.WriteString(entry.Quest.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteByte((byte)entry.State);
            writer.WriteUInt16(entry.Progress);
            writer.WriteUInt16(entry.Count);
        }

        return writer.Position;
    }

    private static bool Contains(QuestLogEntry[] entries, int count, QuestDefinitionId quest)
    {
        for (int index = 0; index < count; index++)
        {
            if (entries[index].Quest == quest)
            {
                return true;
            }
        }

        return false;
    }
}
}
