using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One row of an offer as a trade window shows it: the row's ID on the player's own side and 0 on the partner's,
///     what it holds, how many are offered, and its refine level.
/// </summary>
public readonly struct TradeEntry
{
    public TradeEntry(long inventoryItem, ItemDefinitionId item, uint quantity, byte refineLevel = 0)
    {
        InventoryItem = inventoryItem;
        Item = item;
        Quantity = quantity;
        RefineLevel = refineLevel;
    }

    /// <summary>
    ///     The row's ID on the player's own side; 0 on the partner's, whose rows travel without their IDs.
    /// </summary>
    public long InventoryItem { get; }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     At least 1.
    /// </summary>
    public uint Quantity { get; }

    /// <summary>
    ///     0 until refining exists; above 0 only for a row of one.
    /// </summary>
    public byte RefineLevel { get; }

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
        writer.WriteByte(RefineLevel);
    }

    internal static bool TryRead(ref WireReader reader, bool isPartners, out TradeEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadByte(out byte refineLevel)
            || (isPartners ? inventoryItem != 0 : inventoryItem <= 0)
            || quantity == 0
            || (refineLevel > 0 && quantity > 1)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new TradeEntry(inventoryItem, item, quantity, refineLevel);
        return true;
    }
}
}
