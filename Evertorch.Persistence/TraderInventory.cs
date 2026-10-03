using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     A character's whole inventory: its revision, its coins, and every row with its equipped slot.
/// </summary>
public sealed class TraderInventory
{
    public TraderInventory(long characterId, uint inventoryRevision, long coins, IReadOnlyList<StoredItem> rows)
    {
        CharacterId = characterId;
        InventoryRevision = inventoryRevision;
        Coins = coins;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public long CharacterId { get; }

    public uint InventoryRevision { get; }

    public long Coins { get; }

    public IReadOnlyList<StoredItem> Rows { get; }
}
}
