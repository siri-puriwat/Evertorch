using System;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A character's one inventory change whose commit has not settled (Persistence §5, §7): its kind, the command that
///     asked for it, and its operation ID, with what the answer needs besides: a pickup's reserved drop, the row a swap
///     takes out of its slot, which the ledger does not name, or the item a use restores with.
/// </summary>
public sealed class InventoryOperation
{
    public InventoryOperation(
        InventoryOperationKind kind,
        uint commandSequence,
        Guid operationId,
        ItemDropEntity? drop = null,
        long displacedRow = 0,
        ItemDefinitionId item = default)
    {
        Kind = kind;
        CommandSequence = commandSequence;
        OperationId = operationId;
        Drop = drop;
        DisplacedRow = displacedRow;
        Item = item;
    }

    public InventoryOperationKind Kind { get; }

    public uint CommandSequence { get; }

    public Guid OperationId { get; }

    /// <summary>The reserved drop of a pickup; null for every other kind.</summary>
    public ItemDropEntity? Drop { get; }

    /// <summary>The row a swap takes out of its slot, or 0.</summary>
    public long DisplacedRow { get; }

    /// <summary>The item a use consumes; default for every other kind.</summary>
    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     A pickup of <paramref name="drop" />, whose drop ID is the operation ID.
    /// </summary>
    public static InventoryOperation ForPickup(ItemDropEntity drop, uint commandSequence)
    {
        return new InventoryOperation(InventoryOperationKind.Pickup, commandSequence, drop.DropId, drop);
    }
}
}
