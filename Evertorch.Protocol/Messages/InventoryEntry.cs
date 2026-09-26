using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One inventory row as the owner sees it: its stable ID, what it holds, how many, and the equipment slot it is
///     worn in.
/// </summary>
public readonly struct InventoryEntry
{
    public InventoryEntry(
        long inventoryItem,
        ItemDefinitionId item,
        uint quantity,
        EquipmentSlot slot = EquipmentSlot.None)
    {
        InventoryItem = inventoryItem;
        Item = item;
        Quantity = quantity;
        Slot = slot;
    }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     At least 1 in a snapshot; in a change, 0 means the row was removed.
    /// </summary>
    public uint Quantity { get; }

    /// <summary>
    ///     The slot the row is worn in; <see cref="EquipmentSlot.None" /> when it is not worn, and always for a removed
    ///     row.
    /// </summary>
    public EquipmentSlot Slot { get; }

    internal int GetEncodedLength()
    {
        return sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + sizeof(byte);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteInt64(InventoryItem);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteByte((byte)Slot);
    }

    internal static bool TryRead(ref WireReader reader, bool allowsZero, out InventoryEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadByte(out byte slot)
            || inventoryItem <= 0
            || (quantity == 0 && !allowsZero)
            || slot > (byte)EquipmentSlot.Armor
            || (quantity == 0 && slot != (byte)EquipmentSlot.None)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new InventoryEntry(inventoryItem, item, quantity, (EquipmentSlot)slot);
        return true;
    }

    // A slot holds one row at most, so no message may say two rows are worn in it.
    internal static bool IsEachSlotWornOnce(IReadOnlyList<InventoryEntry> entries)
    {
        bool isWeaponWorn = false;
        bool isArmorWorn = false;
        foreach (InventoryEntry entry in entries)
        {
            if ((entry.Slot == EquipmentSlot.Weapon && isWeaponWorn)
                || (entry.Slot == EquipmentSlot.Armor && isArmorWorn))
            {
                return false;
            }

            isWeaponWorn |= entry.Slot == EquipmentSlot.Weapon;
            isArmorWorn |= entry.Slot == EquipmentSlot.Armor;
        }

        return true;
    }
}
}
