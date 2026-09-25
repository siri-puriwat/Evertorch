using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The status effects on the receiver's own character with the time left of each, sent to its owner alone after
///     the skill list in every baseline and again whenever one starts, is renewed, or ends (Network Protocol §9).
/// </summary>
public sealed class StatusEffects
{
    /// <summary>
    ///     The most effects one message holds, so it fits one reliable message even with the longest IDs.
    /// </summary>
    public const int MaxEntries = 14;

    public StatusEffects(IReadOnlyList<StatusEffectEntry> effects)
    {
        if (effects == null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        if (effects.Count > MaxEntries)
        {
            throw new ArgumentException($"A status list holds at most {MaxEntries} entries.", nameof(effects));
        }

        Effects = effects;
    }

    public IReadOnlyList<StatusEffectEntry> Effects { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out StatusEffects? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StatusEffects)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntries)
        {
            return false;
        }

        var effects = new StatusEffectEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string statusText)
                || !reader.TryReadUInt32(out uint remainingMs)
                || !StatusDefinitionId.TryCreate(statusText, out StatusDefinitionId status)
                || Contains(effects, index, status))
            {
                return false;
            }

            effects[index] = new StatusEffectEntry(status, remainingMs);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new StatusEffects(effects);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (StatusEffectEntry entry in Effects)
        {
            length += WireText.GetEncodedLength(entry.Status.Value, ProtocolLimits.MaxDefinitionIdBytes) + sizeof(uint);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StatusEffects);
        writer.WriteByte((byte)Effects.Count);
        foreach (StatusEffectEntry entry in Effects)
        {
            writer.WriteString(entry.Status.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt32(entry.RemainingMs);
        }

        return writer.Position;
    }

    private static bool Contains(StatusEffectEntry[] effects, int count, StatusDefinitionId status)
    {
        for (int index = 0; index < count; index++)
        {
            if (effects[index].Status == status)
            {
                return true;
            }
        }

        return false;
    }
}
}
