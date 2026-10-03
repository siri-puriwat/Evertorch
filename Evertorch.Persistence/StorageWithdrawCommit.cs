using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One withdrawal to commit (Persistence §5): a quantity of one row of the character's account's storage into its
///     bag, under a new operation ID.
/// </summary>
public sealed class StorageWithdrawCommit
{
    public StorageWithdrawCommit(
        Guid operationId,
        long characterId,
        long storageItemId,
        int quantity,
        IReadOnlyDictionary<string, int> stackLimits,
        int maxRows,
        DateTime at)
    {
        if (quantity < 1 || maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity and rows are at least 1.");
        }

        StackLimits = stackLimits ?? throw new ArgumentNullException(nameof(stackLimits));

        OperationId = operationId;
        CharacterId = characterId;
        StorageItemId = storageItemId;
        Quantity = quantity;
        MaxRows = maxRows;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long StorageItemId { get; }

    public int Quantity { get; }

    /// <summary>
    ///     The stack limit of every item the content knows, by item definition ID: a withdrawal learns which item it
    ///     takes only from the storage row, since the server keeps no view of storage.
    /// </summary>
    public IReadOnlyDictionary<string, int> StackLimits { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
