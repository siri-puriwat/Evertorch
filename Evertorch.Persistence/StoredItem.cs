namespace Evertorch.Persistence
{
/// <summary>
///     One inventory row: a stack of one item definition.
/// </summary>
public sealed class StoredItem
{
    public StoredItem(
        long id,
        string itemDefinitionId,
        int quantity,
        string? equippedSlot = null,
        int refineLevel = 0)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
        EquippedSlot = equippedSlot;
        RefineLevel = refineLevel;
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The equipment slot that holds the row, <c>Weapon</c> or <c>Armor</c>, or null when it is not equipped.
    /// </summary>
    public string? EquippedSlot { get; }

    /// <summary>
    ///     The row's refine level, 0 until refining exists (Persistence §4); it travels with the row wherever it goes.
    /// </summary>
    public int RefineLevel { get; }
}
}
