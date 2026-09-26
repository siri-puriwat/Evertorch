using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
///     One committed inventory change, sent to the owner alone (Network Protocol §9). It applies only to the inventory
///     at <see cref="PriorRevision" />; a client at any other revision asks for a snapshot instead.
/// </summary>
public sealed class InventoryChanged
{
    public const int MaxChanges = InventorySnapshot.MaxEntries;

    public InventoryChanged(uint priorRevision, uint newRevision, IReadOnlyList<InventoryEntry> changes)
    {
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        if (changes.Count == 0 || changes.Count > MaxChanges)
        {
            throw new ArgumentException("A change carries 1 to 12 rows.", nameof(changes));
        }

        PriorRevision = priorRevision;
        NewRevision = newRevision;
    }

    public uint PriorRevision { get; }

    public uint NewRevision { get; }

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
            || !reader.TryReadByte(out byte count)
            || count == 0
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

        message = new InventoryChanged(priorRevision, newRevision, changes);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + 2 * sizeof(uint) + sizeof(byte);
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
        writer.WriteByte((byte)Changes.Count);
        foreach (InventoryEntry change in Changes)
        {
            change.Write(ref writer);
        }

        return writer.Position;
    }
}
}
