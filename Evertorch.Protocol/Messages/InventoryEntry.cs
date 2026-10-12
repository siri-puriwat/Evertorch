using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One inventory row as the owner sees it: its stable ID, what it holds, how many, the equipment slot it is worn in,
///     its refine level, and its cards.
/// </summary>
public readonly struct InventoryEntry
{
    private readonly ItemDefinitionId[]? m_cards;

    public InventoryEntry(
        long inventoryItem,
        ItemDefinitionId item,
        uint quantity,
        EquipmentSlot slot = EquipmentSlot.None,
        byte refineLevel = 0,
        IReadOnlyList<ItemDefinitionId>? cards = null)
    {
        InventoryItem = inventoryItem;
        Item = item;
        Quantity = quantity;
        Slot = slot;
        RefineLevel = refineLevel;
        m_cards = WireCards.Of(cards, quantity);
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

    /// <summary>
    ///     0 until refining exists (Network Protocol §6); above 0 only for a row of one.
    /// </summary>
    public byte RefineLevel { get; }

    /// <summary>
    ///     The cards in the row's slots, in slot order (Network Protocol §6); empty, never null, on a row without any and
    ///     on a default row.
    /// </summary>
    public IReadOnlyList<ItemDefinitionId> Cards => m_cards ?? Array.Empty<ItemDefinitionId>();

    internal int GetEncodedLength()
    {
        return sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + 2 * sizeof(byte)
            + WireCards.GetEncodedLength(Cards);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteInt64(InventoryItem);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteByte((byte)Slot);
        writer.WriteByte(RefineLevel);
        WireCards.Write(ref writer, Cards);
    }

    internal static bool TryRead(ref WireReader reader, bool allowsZero, out InventoryEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadByte(out byte slot)
            || !reader.TryReadByte(out byte refineLevel)
            || !WireCards.TryRead(ref reader, quantity, out ItemDefinitionId[] cards)
            || inventoryItem <= 0
            || (quantity == 0 && !allowsZero)
            || slot > (byte)EquipmentSlot.Armor
            || (quantity == 0 && slot != (byte)EquipmentSlot.None)
            || (refineLevel > 0 && quantity != 1)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new InventoryEntry(inventoryItem, item, quantity, (EquipmentSlot)slot, refineLevel, cards);
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
