namespace Evertorch.Persistence
{
/// <summary>
///     How a pickup ended. When it is committed, the picker's inventory revision and the row that holds the item,
///     both as they are now.
/// </summary>
public sealed class PickupResult
{
    public PickupResult(PickupStatus status, uint inventoryRevision, StoredItem? row)
    {
        Status = status;
        InventoryRevision = inventoryRevision;
        Row = row;
    }

    public PickupStatus Status { get; }

    public uint InventoryRevision { get; }

    public StoredItem? Row { get; }
}
}
