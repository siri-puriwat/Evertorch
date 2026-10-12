using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One row of an account's storage: a stack of one item definition, with its refine level.
/// </summary>
public sealed class StoredStorageItem
{
    public StoredStorageItem(
        long id,
        string itemDefinitionId,
        int quantity,
        int refineLevel = 0,
        IReadOnlyList<string>? cards = null)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId;
        Quantity = quantity;
        RefineLevel = refineLevel;
        Cards = cards ?? Array.Empty<string>();
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }

    /// <summary>
    ///     0 for a row the operation emptied.
    /// </summary>
    public int Quantity { get; }

    public int RefineLevel { get; }

    /// <summary>
    ///     The stored row's cards in slot order; empty for none (Persistence §4).
    /// </summary>
    public IReadOnlyList<string> Cards { get; }
}
}
