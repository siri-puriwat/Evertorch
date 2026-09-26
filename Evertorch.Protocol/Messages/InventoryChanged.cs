using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One committed inventory change, sent to the owner alone (Network Protocol §9). It applies only to the inventory
///     at <see cref="PriorRevision" />; a client at any other revision asks for a snapshot instead. A change in which
///     only the coins moved carries no row.
/// </summary>
public sealed class InventoryChanged
{
    public const int MaxChanges = InventorySnapshot.MaxEntries;

    public InventoryChanged(uint priorRevision, uint newRevision, uint coins, IReadOnlyList<InventoryEntry> changes)
    {
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        if (changes.Count > MaxChanges)
        {
            throw new ArgumentException("A change carries at most 12 rows.", nameof(changes));
        }

        if (coins > ContentLimits.MaxCurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(coins), coins, "Coins stop at the cap.");
        }

        PriorRevision = priorRevision;
        NewRevision = newRevision;
        Coins = coins;
    }

    public uint PriorRevision { get; }

    public uint NewRevision { get; }

    /// <summary>
    ///     The character's coins at <see cref="NewRevision" />.
    /// </summary>
    public uint Coins { get; }

    /// <summary>
    ///     Each row's new state; a quantity of 0 means the row was removed.
    /// </summary>
    public IReadOnlyList<InventoryEntry> Changes { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out InventoryChanged? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.InventoryChanged)
            || !reader.TryReadUInt32(out uint priorRevision)
            || !reader.TryReadUInt32(out uint newRevision)
            || !reader.TryReadUInt32(out uint coins)
            || !reader.TryReadByte(out byte count)
            || coins > ContentLimits.MaxCurrency
            || count > MaxChanges)
        {
            return false;
        }

        var changes = new InventoryEntry[count];
        for (int index = 0; index < count; index++)
        {
            if (!InventoryEntry.TryRead(ref reader, true, out changes[index]))
            {
                return false;
            }
        }

        if (!reader.IsAtEnd || !InventoryEntry.IsEachSlotWornOnce(changes))
        {
            return false;
        }

        message = new InventoryChanged(priorRevision, newRevision, coins, changes);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + 3 * sizeof(uint) + sizeof(byte);
        foreach (InventoryEntry change in Changes)
        {
            length += change.GetEncodedLength();
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InventoryChanged);
        writer.WriteUInt32(PriorRevision);
        writer.WriteUInt32(NewRevision);
        writer.WriteUInt32(Coins);
        writer.WriteByte((byte)Changes.Count);
        foreach (InventoryEntry change in Changes)
        {
            change.Write(ref writer);
        }

        return writer.Position;
    }
}
}
