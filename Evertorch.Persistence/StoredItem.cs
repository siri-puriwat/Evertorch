using System;
using System.Collections.Generic;

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
        int refineLevel = 0,
        IReadOnlyList<string>? cards = null)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
        EquippedSlot = equippedSlot;
        RefineLevel = refineLevel;
        Cards = cards ?? Array.Empty<string>();
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The stored name of the equipment position that holds the row, one of the eight, or null when it is not worn.
    /// </summary>
    public string? EquippedSlot { get; }

    /// <summary>
    ///     The row's refine level, 0 until refining exists (Persistence §4); it travels with the row wherever it goes.
    /// </summary>
    public int RefineLevel { get; }

    /// <summary>
    ///     The row's cards in slot order, the item IDs its instance data holds; empty for none (Persistence §4).
    /// </summary>
    public IReadOnlyList<string> Cards { get; }
}
}
