using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of the owner's whole inventory at a revision (Network Protocol §6, §9). An inventory of up to 100 rows
///     travels as consecutive parts of at most <see cref="MaxEntries" /> rows with the same revision, so every part
///     fits one datagram; the client applies it when the last part arrives. An empty inventory is one empty part.
/// </summary>
public sealed class InventorySnapshot
{
    public const int MaxEntries = 12;

    public InventorySnapshot(uint revision, byte part, byte partCount, IReadOnlyList<InventoryEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (partCount == 0 || part >= partCount || entries.Count > MaxEntries)
        {
            throw new ArgumentException("A part is numbered below its count, and holds at most 12 entries.");
        }

        Revision = revision;
        Part = part;
        PartCount = partCount;
    }

    public uint Revision { get; }

    /// <summary>
    ///     The part's position, from 0.
    /// </summary>
    public byte Part { get; }

    public byte PartCount { get; }

    public IReadOnlyList<InventoryEntry> Entries { get; }

    public bool IsLast => Part == PartCount - 1;

    /// <summary>
    ///     Splits a whole inventory into the consecutive parts that carry it; an empty inventory is one empty part.
    /// </summary>
    public static IReadOnlyList<InventorySnapshot> CreateParts(uint revision, IReadOnlyList<InventoryEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        int partCount = Math.Max(1, (entries.Count + MaxEntries - 1) / MaxEntries);
        if (partCount > byte.MaxValue)
        {
            throw new ArgumentException("An inventory this large cannot be numbered in parts.", nameof(entries));
        }

        var parts = new InventorySnapshot[partCount];
        for (int part = 0; part < partCount; part++)
        {
            int start = part * MaxEntries;
            int count = Math.Min(MaxEntries, entries.Count - start);
            var slice = new InventoryEntry[Math.Max(0, count)];
            for (int index = 0; index < slice.Length; index++)
            {
                slice[index] = entries[start + index];
            }

            parts[part] = new InventorySnapshot(revision, (byte)part, (byte)partCount, slice);
        }

        return parts;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out InventorySnapshot? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.InventorySnapshot)
            || !reader.TryReadUInt32(out uint revision)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadByte(out byte count)
            || partCount == 0
            || part >= partCount
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new InventoryEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!InventoryEntry.TryRead(ref reader, false, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd || !InventoryEntry.IsEachSlotWornOnce(entries))
        {
            return false;
        }

        message = new InventorySnapshot(revision, part, partCount, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(uint) + 3 * sizeof(byte);
        foreach (InventoryEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InventorySnapshot);
        writer.WriteUInt32(Revision);
        writer.WriteByte(Part);
        writer.WriteByte(PartCount);
        writer.WriteByte((byte)Entries.Count);
        foreach (InventoryEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }
}
}
