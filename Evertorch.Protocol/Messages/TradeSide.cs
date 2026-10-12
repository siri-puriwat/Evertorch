using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of one side of the player's open trade as it stands (Network Protocol §6, §9): whose it is, whether it
///     is locked and confirmed, its coins, and its rows, at most <see cref="MaxEntries" /> in all. Sent to both traders
///     whenever the side changes, as consecutive parts of at most <see cref="ProtocolLimits.MaxReliableMessageBytes" />
///     bytes with the same owner, flags, and coins, which the client assembles as it does a snapshot's into one whole
///     side that no message carries; the partner's rows come without their IDs.
/// </summary>
public sealed class TradeSide
{
    public const int MaxEntries = 10;

    private const int HeaderLength = sizeof(ushort) + 5 * sizeof(byte) + sizeof(uint) + sizeof(byte);

    public TradeSide(
        TradeSideOwner owner,
        bool isLocked,
        bool isConfirmed,
        uint coins,
        IReadOnlyList<TradeEntry> entries,
        byte part = 0,
        byte partCount = 1)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (entries.Count > MaxEntries || coins > ContentLimits.MaxCurrency)
        {
            throw new ArgumentException("A side offers at most ten rows and coins up to the cap.");
        }

        if (partCount == 0 || part >= partCount)
        {
            throw new ArgumentException("A part is numbered below its count.", nameof(part));
        }

        Owner = owner;
        IsLocked = isLocked;
        IsConfirmed = isConfirmed;
        Coins = coins;
        Part = part;
        PartCount = partCount;
    }

    public TradeSideOwner Owner { get; }

    public bool IsLocked { get; }

    public bool IsConfirmed { get; }

    public uint Coins { get; }

    /// <summary>
    ///     The part's position, from 0.
    /// </summary>
    public byte Part { get; }

    public byte PartCount { get; }

    public IReadOnlyList<TradeEntry> Entries { get; }

    public bool IsLast => Part == PartCount - 1;

    /// <summary>
    ///     Splits a side into the consecutive parts that carry it, each filled in row order until the next row would pass
    ///     a message's bytes; a side without rows is one empty part.
    /// </summary>
    public static IReadOnlyList<TradeSide> CreateParts(
        TradeSideOwner owner,
        bool isLocked,
        bool isConfirmed,
        uint coins,
        IReadOnlyList<TradeEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException("A side offers at most ten rows.", nameof(entries));
        }

        List<List<TradeEntry>> slices = WireParts.Pack(
            entries,
            HeaderLength,
            MaxEntries,
            entry => entry.GetEncodedLength());
        var parts = new TradeSide[slices.Count];
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new TradeSide(
                owner,
                isLocked,
                isConfirmed,
                coins,
                slices[part],
                (byte)part,
                (byte)parts.Length);
        }

        return parts;
    }

    /// <summary>
    ///     False for a malformed message, including one longer than a reliable message, an owner or a flag out of range,
    ///     a part at or past its count, coins past the cap, more than ten rows, a row ID on the partner's side or none on
    ///     the player's own, a quantity of 0, and a refine level or a card on a row of more than one.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeSide? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > ProtocolLimits.MaxReliableMessageBytes
            || !reader.TryReadOpcode(MessageOpcode.TradeSide)
            || !reader.TryReadByte(out byte ownerValue)
            || !reader.TryReadByte(out byte locked)
            || !reader.TryReadByte(out byte confirmed)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadUInt32(out uint coins)
            || !reader.TryReadByte(out byte count)
            || locked > 1
            || confirmed > 1
            || partCount == 0
            || part >= partCount
            || coins > ContentLimits.MaxCurrency
            || count > MaxEntries)
        {
            return false;
        }

        var owner = (TradeSideOwner)ownerValue;
        if (!WireEnums.IsDefined(owner))
        {
            return false;
        }

        var entries = new TradeEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!TradeEntry.TryRead(ref reader, owner == TradeSideOwner.Partner, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new TradeSide(owner, locked == 1, confirmed == 1, coins, entries, part, partCount);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = HeaderLength;
        foreach (TradeEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeSide);
        writer.WriteByte((byte)Owner);
        writer.WriteByte(IsLocked ? (byte)1 : (byte)0);
        writer.WriteByte(IsConfirmed ? (byte)1 : (byte)0);
        writer.WriteByte(Part);
        writer.WriteByte(PartCount);
        writer.WriteUInt32(Coins);
        writer.WriteByte((byte)Entries.Count);
        foreach (TradeEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }
}
}
