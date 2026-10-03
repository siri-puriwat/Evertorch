namespace Evertorch.Persistence
{
/// <summary>
///     How a deposit or a withdrawal ended (Persistence §5): the character's inventory revision and coins, and, when
///     committed, the bag's row and the storage's row the operation changed, each as it is now (a row it emptied at 0),
///     with the storage's revision.
/// </summary>
public sealed class StorageResult
{
    public StorageResult(
        InventoryStatus status,
        uint inventoryRevision,
        long coins,
        StoredItem? row,
        uint storageRevision,
        StoredStorageItem? storageRow)
    {
        Status = status;
        InventoryRevision = inventoryRevision;
        Coins = coins;
        Row = row;
        StorageRevision = storageRevision;
        StorageRow = storageRow;
    }

    public InventoryStatus Status { get; }

    public uint InventoryRevision { get; }

    public long Coins { get; }

    public StoredItem? Row { get; }

    public uint StorageRevision { get; }

    public StoredStorageItem? StorageRow { get; }
}
}
