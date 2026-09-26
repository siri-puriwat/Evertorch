using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     How an inventory operation ended (Persistence §5). When it is committed, the character's inventory revision and
///     every row the operation changed, each with its equipped slot, as they are now; a row the operation emptied
///     reports a quantity of 0.
/// </summary>
public sealed class InventoryResult
{
    public InventoryResult(InventoryStatus status, uint inventoryRevision, IReadOnlyList<StoredItem> rows)
    {
        Status = status;
        InventoryRevision = inventoryRevision;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public InventoryStatus Status { get; }

    public uint InventoryRevision { get; }

    public IReadOnlyList<StoredItem> Rows { get; }
}
}
