using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of the account's storage at a revision, with the Storekeeper's deposit fee (Network Protocol §6, §9).
///     Up to 300 rows travel as consecutive parts of at most <see cref="MaxEntries" /> rows with the same revision and
///     fee; the client takes it when the last part arrives. An empty storage is one empty part.
/// </summary>
public sealed class StorageSnapshot
{
    public const int MaxEntries = 12;

    public StorageSnapshot(
        uint revision,
        uint depositFee,
        byte part,
        byte partCount,
        IReadOnlyList<StorageEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (partCount == 0 || part >= partCount || entries.Count > MaxEntries)
        {
            throw new ArgumentException("A part is numbered below its count, and holds at most 12 entries.");
        }

        if (depositFee > ContentLimits.MaxDepositFee)
        {
            throw new ArgumentOutOfRangeException(nameof(depositFee), depositFee, "The fee stops at its cap.");
        }

        Revision = revision;
        DepositFee = depositFee;
        Part = part;
        PartCount = partCount;
    }

    public uint Revision { get; }

    /// <summary>
    ///     What a deposit costs at this Storekeeper, the same in every part; it travels on purpose, as a shop's prices do.
    /// </summary>
    public uint DepositFee { get; }

    public byte Part { get; }

    public byte PartCount { get; }

    public IReadOnlyList<StorageEntry> Entries { get; }

    public bool IsLast => Part == PartCount - 1;

    /// <summary>
    ///     Splits a whole storage into the consecutive parts that carry it; an empty storage is one empty part.
    /// </summary>
    public static IReadOnlyList<StorageSnapshot> CreateParts(
        uint revision,
        uint depositFee,
        IReadOnlyList<StorageEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        int partCount = Math.Max(1, (entries.Count + MaxEntries - 1) / MaxEntries);
        if (partCount > byte.MaxValue)
        {
            throw new ArgumentException("A storage this large cannot be numbered in parts.", nameof(entries));
        }

        var parts = new StorageSnapshot[partCount];
        for (int part = 0; part < partCount; part++)
        {
            int start = part * MaxEntries;
            int count = Math.Min(MaxEntries, entries.Count - start);
            var slice = new StorageEntry[Math.Max(0, count)];
            for (int index = 0; index < slice.Length; index++)
            {
                slice[index] = entries[start + index];
            }

            parts[part] = new StorageSnapshot(revision, depositFee, (byte)part, (byte)partCount, slice);
        }

        return parts;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out StorageSnapshot? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.StorageSnapshot)
            || !reader.TryReadUInt32(out uint revision)
            || !reader.TryReadUInt32(out uint fee)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadByte(out byte count)
            || fee > ContentLimits.MaxDepositFee
            || partCount == 0
            || part >= partCount
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new StorageEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!StorageEntry.TryRead(ref reader, false, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new StorageSnapshot(revision, fee, part, partCount, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + 2 * sizeof(uint) + 3 * sizeof(byte);
        foreach (StorageEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.StorageSnapshot);
        writer.WriteUInt32(Revision);
        writer.WriteUInt32(DepositFee);
        writer.WriteByte(Part);
        writer.WriteByte(PartCount);
        writer.WriteByte((byte)Entries.Count);
        foreach (StorageEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }
}
}
