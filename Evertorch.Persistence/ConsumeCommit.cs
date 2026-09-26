using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One use of a consumable to commit (Persistence §5): one unit of the row, under a new operation ID.
/// </summary>
public sealed class ConsumeCommit
{
    public ConsumeCommit(Guid operationId, long characterId, long inventoryItemId, DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long InventoryItemId { get; }

    public DateTime At { get; }
}
}
