using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One row of the account's storage as its owner sees it: its stable ID, what it holds, how many, and its refine
///     level.
/// </summary>
public readonly struct StorageEntry
{
    public StorageEntry(long storageItem, ItemDefinitionId item, uint quantity, byte refineLevel = 0)
    {
        StorageItem = storageItem;
        Item = item;
        Quantity = quantity;
        RefineLevel = refineLevel;
    }

    public long StorageItem { get; }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     At least 1 in a snapshot; in a change, 0 means the row was removed.
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
        writer.WriteInt64(StorageItem);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteByte(RefineLevel);
    }

    internal static bool TryRead(ref WireReader reader, bool allowsZero, out StorageEntry entry)
    {
        entry = default;
        if (!reader.TryReadInt64(out long storageItem)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadByte(out byte refineLevel)
            || storageItem <= 0
            || (quantity == 0 && !allowsZero)
            || (refineLevel > 0 && quantity != 1)
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new StorageEntry(storageItem, item, quantity, refineLevel);
        return true;
    }
}
}
