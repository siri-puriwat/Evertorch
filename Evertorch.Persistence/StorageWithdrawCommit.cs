using System;

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
        int stackLimit,
        int maxRows,
        DateTime at)
    {
        if (quantity < 1 || stackLimit < 1 || maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity, stack limit, and rows are at least 1.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        StorageItemId = storageItemId;
        Quantity = quantity;
        StackLimit = stackLimit;
        MaxRows = maxRows;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long StorageItemId { get; }

    public int Quantity { get; }

    public int StackLimit { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
