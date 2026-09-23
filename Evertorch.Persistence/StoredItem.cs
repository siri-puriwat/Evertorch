namespace Evertorch.Persistence
{
/// <summary>
///     One inventory row: a stack of one item definition.
/// </summary>
public sealed class StoredItem
{
    public StoredItem(long id, string itemDefinitionId, int quantity)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }
}
}
