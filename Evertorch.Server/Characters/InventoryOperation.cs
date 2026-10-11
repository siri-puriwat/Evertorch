using System;
using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     A character's one inventory change whose commit has not settled (Persistence §5, §7): its kind, the command that
///     asked for it, and its operation ID, with what the answer needs besides: a pickup's reserved drop, the row the
///     ledger does not name but the answer reports, a swap's displaced row or a refine's ore, the item a use restores
///     with, what a purchase or a sale moves, the quest a turn-in completes, the job a change makes, or the listing a
///     collection ends.
/// </summary>
public sealed class InventoryOperation
{
    public InventoryOperation(
        InventoryOperationKind kind,
        uint commandSequence,
        Guid operationId,
        ItemDropEntity? drop = null,
        ReportedRow? reported = null,
        ItemDefinitionId item = default,
        int quantity = 0,
        long coins = 0,
        QuestDefinitionId quest = default,
        JobDefinitionId job = default,
        long listing = 0)
    {
        Kind = kind;
        CommandSequence = commandSequence;
        OperationId = operationId;
        Drop = drop;
        Reported = reported;
        Item = item;
        Quantity = quantity;
        Coins = coins;
        Quest = quest;
        Job = job;
        Listing = listing;
    }

    public InventoryOperationKind Kind { get; }

    public uint CommandSequence { get; }

    public Guid OperationId { get; }

    /// <summary>The reserved drop of a pickup; null for every other kind.</summary>
    public ItemDropEntity? Drop { get; }

    /// <summary>
    ///     The row the answer reports beside the ledger's, under its own item: the row a swap takes out of its slot, or
    ///     a refine's ore; null when there is none.
    /// </summary>
    public ReportedRow? Reported { get; }

    /// <summary>The item a use consumes, or a purchase or a sale moves; default for every other kind.</summary>
    public ItemDefinitionId Item { get; }

    /// <summary>How many a purchase or a sale moves; 0 for every other kind.</summary>
    public int Quantity { get; }

    /// <summary>The coins a purchase costs, or a sale or a turn-in fetches; 0 for every other kind.</summary>
    public long Coins { get; }

    /// <summary>The quest a turn-in completes; default for every other kind.</summary>
    public QuestDefinitionId Quest { get; }

    /// <summary>The first job a change makes; default for every other kind.</summary>
    public JobDefinitionId Job { get; }

    /// <summary>The listing a collection ends; 0 for every other kind.</summary>
    public long Listing { get; }

    /// <summary>
    ///     A pickup of <paramref name="drop" />, whose drop ID is the operation ID.
    /// </summary>
    public static InventoryOperation ForPickup(ItemDropEntity drop, uint commandSequence)
    {
        return new InventoryOperation(InventoryOperationKind.Pickup, commandSequence, drop.DropId, drop);
    }
}
}
