using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One position of an inspection (Network Protocol §6): the position and what it holds, an empty item when nothing.
/// </summary>
public readonly struct InspectedEntry
{
    public InspectedEntry(EquipmentSlot slot, InspectedItem item)
    {
        if (!EquipmentPositions.IsPosition(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "An entry names one of the eight positions.");
        }

        Slot = slot;
        Item = item;
    }

    public EquipmentSlot Slot { get; }

    public InspectedItem Item { get; }

    internal int GetEncodedLength()
    {
        return sizeof(byte) + Item.GetEncodedLength();
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteByte((byte)Slot);
        Item.Write(ref writer);
    }

    /// <summary>False for a slot outside 1 to 8 and for an item <see cref="InspectedItem" /> refuses.</summary>
    internal static bool TryRead(ref WireReader reader, out InspectedEntry entry)
    {
        entry = default;
        if (!reader.TryReadByte(out byte slot)
            || !EquipmentPositions.IsPosition((EquipmentSlot)slot)
            || !InspectedItem.TryRead(ref reader, out InspectedItem item))
        {
            return false;
        }

        entry = new InspectedEntry((EquipmentSlot)slot, item);
        return true;
    }
}
}
