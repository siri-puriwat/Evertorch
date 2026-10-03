using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One deposit to commit (Persistence §5): a quantity of one of the character's bag rows into its account's
///     storage, for the Storekeeper's fee, under a new operation ID.
/// </summary>
public sealed class StorageDepositCommit
{
    public StorageDepositCommit(
        Guid operationId,
        long characterId,
        long inventoryItemId,
        int quantity,
        int fee,
        int stackLimit,
        int maxStorageRows,
        DateTime at)
    {
        if (quantity < 1 || fee < 0 || stackLimit < 1 || maxStorageRows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity, stack limit, and rows are at least 1, and the fee is not negative.");
        }

        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        Quantity = quantity;
        Fee = fee;
        StackLimit = stackLimit;
        MaxStorageRows = maxStorageRows;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long InventoryItemId { get; }

    public int Quantity { get; }

    public int Fee { get; }

    public int StackLimit { get; }

    public int MaxStorageRows { get; }

    public DateTime At { get; }
}
}
