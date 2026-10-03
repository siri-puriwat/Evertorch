using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One side of the player's open trade as it stands (Network Protocol §6, §9): whose it is, whether it is locked
///     and confirmed, its coins, and its rows, at most <see cref="MaxEntries" />. Sent to both traders whenever the
///     side changes; the partner's rows come without their IDs.
/// </summary>
public sealed class TradeSide
{
    public const int MaxEntries = 10;

    public TradeSide(
        TradeSideOwner owner,
        bool isLocked,
        bool isConfirmed,
        uint coins,
        IReadOnlyList<TradeEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (entries.Count > MaxEntries || coins > ContentLimits.MaxCurrency)
        {
            throw new ArgumentException("A side offers at most ten rows and coins up to the cap.");
        }

        Owner = owner;
        IsLocked = isLocked;
        IsConfirmed = isConfirmed;
        Coins = coins;
    }

    public TradeSideOwner Owner { get; }

    public bool IsLocked { get; }

    public bool IsConfirmed { get; }

    public uint Coins { get; }

    public IReadOnlyList<TradeEntry> Entries { get; }

    /// <summary>
    ///     False for a malformed message, including an owner or a flag out of range, coins past the cap, more than ten
    ///     rows, a row ID on the partner's side or none on the player's own, a quantity of 0, and a refine level on a row
    ///     of more than one.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeSide? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeSide)
            || !reader.TryReadByte(out byte ownerValue)
            || !reader.TryReadByte(out byte locked)
            || !reader.TryReadByte(out byte confirmed)
            || !reader.TryReadUInt32(out uint coins)
            || !reader.TryReadByte(out byte count)
            || locked > 1
            || confirmed > 1
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

        message = new TradeSide(owner, locked == 1, confirmed == 1, coins, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + 3 * sizeof(byte) + sizeof(uint) + sizeof(byte);
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
