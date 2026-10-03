namespace Evertorch.Persistence
{
/// <summary>
///     One row of an account's storage: a stack of one item definition, with its refine level.
/// </summary>
public sealed class StoredStorageItem
{
    public StoredStorageItem(long id, string itemDefinitionId, int quantity, int refineLevel = 0)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
        RefineLevel = refineLevel;
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    /// <summary>
    ///     0 for a row the operation emptied.
    /// </summary>
    public int Quantity { get; }

    public int RefineLevel { get; }
}
}
