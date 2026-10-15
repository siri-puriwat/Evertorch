using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of what the player <see cref="Entity" /> wears, sent to the asker of an <see cref="InspectPlayer" /> alone
///     (Network Protocol §6, §9); the inspected player hears nothing. The eight positions travel in position order over
///     one to eight parts of at most <see cref="ProtocolLimits.MaxReliableMessageBytes" /> bytes, each naming the request
///     it answers and the appearance revision it was read at, every position once over all the parts.
/// </summary>
public sealed class InspectedEquipment
{
    public const int MaxParts = 8;

    public const int MaxEntries = 8;

    private const int HeaderLength = sizeof(ushort) + sizeof(long) + 2 * sizeof(uint) + 3 * sizeof(byte);

    public InspectedEquipment(
        EntityId entity,
        uint commandSequence,
        uint revision,
        byte part,
        byte partCount,
        IReadOnlyList<InspectedEntry> entries)
    {
        if (entity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "A player's entity ID is at least 1.");
        }

        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (partCount == 0 || partCount > MaxParts || part >= partCount || entries.Count == 0
            || entries.Count > MaxEntries || !IsEachSlotOnce(entries))
        {
            throw new ArgumentException(
                "A part is numbered below its count of at most 8, and holds 1 to 8 entries of distinct positions.");
        }

        Entity = entity;
        CommandSequence = commandSequence;
        Revision = revision;
        Part = part;
        PartCount = partCount;
        if (GetEncodedLength() > ProtocolLimits.MaxReliableMessageBytes)
        {
            throw new ArgumentException("A part fits one reliable message.", nameof(entries));
        }
    }

    public EntityId Entity { get; }

    /// <summary>The sequence of the <see cref="InspectPlayer" /> this answers, the same in every part.</summary>
    public uint CommandSequence { get; }

    /// <summary>The inspected player's appearance revision when its bag was read, the same in every part.</summary>
    public uint Revision { get; }

    /// <summary>The part's position, from 0.</summary>
    public byte Part { get; }

    public byte PartCount { get; }

    public IReadOnlyList<InspectedEntry> Entries { get; }

    /// <summary>
    ///     The eight positions of <paramref name="worn" />, in position order, in the consecutive parts that carry them,
    ///     each filled until the next position would pass a part's bytes.
    /// </summary>
    public static IReadOnlyList<InspectedEquipment> CreateParts(
        EntityId entity,
        uint commandSequence,
        uint revision,
        IReadOnlyList<InspectedEntry> worn)
    {
        if (worn == null)
        {
            throw new ArgumentNullException(nameof(worn));
        }

        List<List<InspectedEntry>> slices =
            WireParts.Pack(worn, HeaderLength, MaxEntries, entry => entry.GetEncodedLength());
        var parts = new InspectedEquipment[slices.Count];
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new InspectedEquipment(
                entity,
                commandSequence,
                revision,
                (byte)part,
                (byte)parts.Length,
                slices[part]);
        }

        return parts;
    }

    /// <summary>
    ///     False for a malformed part, including an entity ID below 1, a part at or past its count, a count of 0 or above
    ///     8, no entry or more than 8, a slot outside 1 to 8 or twice in the part, and a level or a card on an empty
    ///     entry.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out InspectedEquipment? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > ProtocolLimits.MaxReliableMessageBytes
            || !reader.TryReadOpcode(MessageOpcode.InspectedEquipment)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadUInt32(out uint commandSequence)
            || !reader.TryReadUInt32(out uint revision)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadByte(out byte count)
            || entity <= 0
            || partCount == 0
            || partCount > MaxParts
            || part >= partCount
            || count == 0
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new InspectedEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!InspectedEntry.TryRead(ref reader, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd || !IsEachSlotOnce(entries))
        {
            return false;
        }

        message = new InspectedEquipment(new EntityId(entity), commandSequence, revision, part, partCount, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = HeaderLength;
        foreach (InspectedEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InspectedEquipment);
        writer.WriteInt64(Entity.Value);
        writer.WriteUInt32(CommandSequence);
        writer.WriteUInt32(Revision);
        writer.WriteByte(Part);
        writer.WriteByte(PartCount);
        writer.WriteByte((byte)Entries.Count);
        foreach (InspectedEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }

    private static bool IsEachSlotOnce(IReadOnlyList<InspectedEntry> entries)
    {
        int seen = 0;
        foreach (InspectedEntry entry in entries)
        {
            int position = 1 << (int)entry.Slot;
            if ((seen & position) != 0)
            {
                return false;
            }

            seen |= position;
        }

        return true;
    }
}
}
