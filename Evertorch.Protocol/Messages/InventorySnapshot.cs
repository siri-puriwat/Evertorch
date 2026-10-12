using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of the owner's whole inventory at a revision (Network Protocol §6, §9). An inventory of up to 100 rows
///     travels as consecutive parts of at most <see cref="MaxEntries" /> rows and
///     <see cref="ProtocolLimits.MaxReliableMessageBytes" /> bytes with the same revision and coins, so every part fits
///     one datagram however many cards its rows hold; the client applies it when the last part arrives. An empty
///     inventory is one empty part.
/// </summary>
public sealed class InventorySnapshot
{
    public const int MaxEntries = 12;

    private const int HeaderLength = sizeof(ushort) + 2 * sizeof(uint) + 3 * sizeof(byte);

    public InventorySnapshot(
        uint revision,
        uint coins,
        byte part,
        byte partCount,
        IReadOnlyList<InventoryEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (partCount == 0 || part >= partCount || entries.Count > MaxEntries)
        {
            throw new ArgumentException("A part is numbered below its count, and holds at most 12 entries.");
        }

        if (coins > ContentLimits.MaxCurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(coins), coins, "Coins stop at the cap.");
        }

        Revision = revision;
        Coins = coins;
        Part = part;
        PartCount = partCount;
        if (GetEncodedLength() > ProtocolLimits.MaxReliableMessageBytes)
        {
            throw new ArgumentException("A part fits one reliable message.", nameof(entries));
        }
    }

    public uint Revision { get; }

    /// <summary>
    ///     The character's coins at <see cref="Revision" />, the same in every part.
    /// </summary>
    public uint Coins { get; }

    /// <summary>
    ///     The part's position, from 0.
    /// </summary>
    public byte Part { get; }

    public byte PartCount { get; }

    public IReadOnlyList<InventoryEntry> Entries { get; }

    public bool IsLast => Part == PartCount - 1;

    /// <summary>
    ///     Splits a whole inventory into the consecutive parts that carry it, each filled in row order until the next row
    ///     would pass a part's rows or bytes; an empty inventory is one empty part.
    /// </summary>
    public static IReadOnlyList<InventorySnapshot> CreateParts(
        uint revision,
        uint coins,
        IReadOnlyList<InventoryEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        List<List<InventoryEntry>> slices = WireParts.Pack(
            entries,
            HeaderLength,
            MaxEntries,
            entry => entry.GetEncodedLength());
        var parts = new InventorySnapshot[slices.Count];
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new InventorySnapshot(revision, coins, (byte)part, (byte)parts.Length, slices[part]);
        }

        return parts;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out InventorySnapshot? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > ProtocolLimits.MaxReliableMessageBytes
            || !reader.TryReadOpcode(MessageOpcode.InventorySnapshot)
            || !reader.TryReadUInt32(out uint revision)
            || !reader.TryReadUInt32(out uint coins)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadByte(out byte count)
            || coins > ContentLimits.MaxCurrency
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

        message = new InventorySnapshot(revision, coins, part, partCount, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = HeaderLength;
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
        writer.WriteUInt32(Coins);
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
