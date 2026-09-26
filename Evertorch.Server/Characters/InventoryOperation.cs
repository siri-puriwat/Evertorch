using System;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A character's one inventory change whose commit has not settled (Persistence §5, §7): its kind, the command that
///     asked for it, and its operation ID, with what the answer needs besides: a pickup's reserved drop, the row a swap
///     takes out of its slot, which the ledger does not name, the item a use restores with, what a trade moves, or
///     the quest a turn-in completes.
/// </summary>
public sealed class InventoryOperation
{
    public InventoryOperation(
        InventoryOperationKind kind,
        uint commandSequence,
        Guid operationId,
        ItemDropEntity? drop = null,
        long displacedRow = 0,
        ItemDefinitionId item = default,
        int quantity = 0,
        long coins = 0,
        QuestDefinitionId quest = default)
    {
        Kind = kind;
        CommandSequence = commandSequence;
        OperationId = operationId;
        Drop = drop;
        DisplacedRow = displacedRow;
        Item = item;
        Quantity = quantity;
        Coins = coins;
        Quest = quest;
    }

    public InventoryOperationKind Kind { get; }

    public uint CommandSequence { get; }

    public Guid OperationId { get; }

    /// <summary>The reserved drop of a pickup; null for every other kind.</summary>
    public ItemDropEntity? Drop { get; }

    /// <summary>The row a swap takes out of its slot, or 0.</summary>
    public long DisplacedRow { get; }

    /// <summary>The item a use consumes, or a trade buys or sells; default for every other kind.</summary>
    public ItemDefinitionId Item { get; }

    /// <summary>How many a trade buys or sells; 0 for every other kind.</summary>
    public int Quantity { get; }

    /// <summary>The coins a purchase costs, or a sale or a turn-in fetches; 0 for every other kind.</summary>
    public long Coins { get; }

    /// <summary>The quest a turn-in completes; default for every other kind.</summary>
    public QuestDefinitionId Quest { get; }

    /// <summary>
    ///     A pickup of <paramref name="drop" />, whose drop ID is the operation ID.
    /// </summary>
    public static InventoryOperation ForPickup(ItemDropEntity drop, uint commandSequence)
    {
        return new InventoryOperation(InventoryOperationKind.Pickup, commandSequence, drop.DropId, drop);
    }
}
}
