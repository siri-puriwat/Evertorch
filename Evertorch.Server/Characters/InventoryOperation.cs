using System;

namespace Evertorch.Server
{
/// <summary>
///     A character's one inventory change whose commit has not settled (Persistence §5, §7): its kind, the command that
///     asked for it, and its operation ID, with what the answer needs besides: a pickup's reserved drop, or the row a
///     swap takes out of its slot, which the ledger does not name.
/// </summary>
public sealed class InventoryOperation
{
    public InventoryOperation(
        InventoryOperationKind kind,
        uint commandSequence,
        Guid operationId,
        ItemDropEntity? drop = null,
        long displacedRow = 0)
    {
        Kind = kind;
        CommandSequence = commandSequence;
        OperationId = operationId;
        Drop = drop;
        DisplacedRow = displacedRow;
    }

    public InventoryOperationKind Kind { get; }

    public uint CommandSequence { get; }

    public Guid OperationId { get; }

    /// <summary>The reserved drop of a pickup; null for every other kind.</summary>
    public ItemDropEntity? Drop { get; }

    /// <summary>The row a swap takes out of its slot, or 0.</summary>
    public long DisplacedRow { get; }

    /// <summary>
    ///     A pickup of <paramref name="drop" />, whose drop ID is the operation ID.
    /// </summary>
    public static InventoryOperation ForPickup(ItemDropEntity drop, uint commandSequence)
    {
        return new InventoryOperation(InventoryOperationKind.Pickup, commandSequence, drop.DropId, drop);
    }
}
}
