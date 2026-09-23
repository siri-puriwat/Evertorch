using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One inventory row as the owner sees it: its stable ID, what it holds, and how many.
/// </summary>
public readonly struct InventoryEntry
{
    public InventoryEntry(long inventoryItem, ItemDefinitionId item, uint quantity)
    {
        InventoryItem = inventoryItem;
        Item = item;
        Quantity = quantity;
    }

    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     At least 1 in a snapshot; in a change, 0 means the row was removed.
    /// </summary>
    public uint Quantity { get; }

    internal int GetEncodedLength()
    {
        return sizeof(long) + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes) + sizeof(uint);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteInt64(InventoryItem);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
    }

    internal static bool TryRead(ref WireReader reader, bool allowsZero, out InventoryEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || inventoryItem <= 0
            || (quantity == 0 && !allowsZero)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new InventoryEntry(inventoryItem, item, quantity);
        return true;
    }
}
}
