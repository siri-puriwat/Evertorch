using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
///     Entries of the market board for the player who read it (Network Protocol §6, §9). The entries are upserts: a
///     board of more items than <see cref="MaxEntries" /> arrives in several messages, and none is the whole board.
/// </summary>
public sealed class MarketBoard
{
    /// <summary>
    ///     The most entries one message holds: 74 bytes each with the longest ID, within one reliable message.
    /// </summary>
    public const int MaxEntries = 13;

    public MarketBoard(IReadOnlyList<MarketBoardEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (entries.Count == 0 || entries.Count > MaxEntries)
        {
            throw new ArgumentException("A board message holds 1 to 13 entries.", nameof(entries));
        }

        foreach (MarketBoardEntry entry in entries)
        {
            if (entry.Price == 0)
            {
                throw new ArgumentException("A price is at least 1 coin.", nameof(entries));
            }
        }
    }

    public IReadOnlyList<MarketBoardEntry> Entries { get; }

    /// <summary>
    ///     Splits a board into the messages that carry it, at most <see cref="MaxEntries" /> entries each; an empty
    ///     board is no message.
    /// </summary>
    public static IReadOnlyList<MarketBoard> CreateParts(IReadOnlyList<MarketBoardEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        var parts = new List<MarketBoard>();
        for (int start = 0; start < entries.Count; start += MaxEntries)
        {
            int count = Math.Min(MaxEntries, entries.Count - start);
            var slice = new MarketBoardEntry[count];
            for (int index = 0; index < count; index++)
            {
                slice[index] = entries[start + index];
            }

            parts.Add(new MarketBoard(slice));
        }

        return parts;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out MarketBoard? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketBoard)
            || !reader.TryReadByte(out byte count)
            || count == 0
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new MarketBoardEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!MarketBoardEntry.TryRead(ref reader, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new MarketBoard(entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (MarketBoardEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketBoard);
        writer.WriteByte((byte)Entries.Count);
        foreach (MarketBoardEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }
}
}
