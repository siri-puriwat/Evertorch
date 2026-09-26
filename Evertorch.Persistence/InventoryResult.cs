using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     How an inventory operation ended (Persistence §5). When it is committed, the character's inventory revision, its
///     coins, and every row the operation changed, each with its equipped slot, as they are now; a row the operation
///     emptied reports a quantity of 0.
/// </summary>
public sealed class InventoryResult
{
    public InventoryResult(InventoryStatus status, uint inventoryRevision, long coins, IReadOnlyList<StoredItem> rows)
    {
        Status = status;
        InventoryRevision = inventoryRevision;
        Coins = coins;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public InventoryStatus Status { get; }

    public uint InventoryRevision { get; }

    /// <summary>
    ///     The character's coins as they are now; 0 when the operation was another character's.
    /// </summary>
    public long Coins { get; }

    public IReadOnlyList<StoredItem> Rows { get; }
}
}
