namespace Evertorch.Persistence
{
/// <summary>
///     One inventory row: a stack of one item definition.
/// </summary>
public sealed class StoredItem
{
    public StoredItem(long id, string itemDefinitionId, int quantity, string? equippedSlot = null)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
        EquippedSlot = equippedSlot;
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The equipment slot that holds the row, <c>Weapon</c> or <c>Armor</c>, or null when it is not equipped.
    /// </summary>
    public string? EquippedSlot { get; }
}
}
