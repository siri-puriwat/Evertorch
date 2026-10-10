using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
///     Every listing of the player who read the board, after the board's entries; the client takes it whole, so it also
///     ends a read (Network Protocol §6, §9).
/// </summary>
public sealed class MarketMine
{
    /// <summary>
    ///     A character's most listings (Gameplay Systems §11.5): 90 bytes each with the longest ID.
    /// </summary>
    public const int MaxEntries = 8;

    public MarketMine(IReadOnlyList<MarketListingEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException("A character holds at most 8 listings.", nameof(entries));
        }
    }

    public IReadOnlyList<MarketListingEntry> Entries { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out MarketMine? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketMine)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new MarketListingEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!MarketListingEntry.TryRead(ref reader, out entries[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new MarketMine(entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(byte);
        foreach (MarketListingEntry entry in Entries)
        {
            length += entry.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketMine);
        writer.WriteByte((byte)Entries.Count);
        foreach (MarketListingEntry entry in Entries)
        {
            entry.Write(ref writer);
        }

        return writer.Position;
    }
}
}
