using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One card to take out at the Smith (Persistence §5): the row, the item the tick thread saw, the slot and the card
///     there, the card's fee, and what the bag allows the card, under a new operation ID. The store checks the row, the
///     card, the coins, and the room again, takes the fee, and puts the card in the bag.
/// </summary>
public sealed class RemoveCardCommit
{
    public RemoveCardCommit(
        Guid operationId,
        long characterId,
        long inventoryItemId,
        string itemDefinitionId,
        int slot,
        string cardDefinitionId,
        long fee,
        int cardStackLimit,
        int maxRows,
        DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Slot = slot;
        CardDefinitionId = cardDefinitionId ?? throw new ArgumentNullException(nameof(cardDefinitionId));
        Fee = fee;
        CardStackLimit = cardStackLimit;
        MaxRows = maxRows;
        At = at;
        ReportRows = new[] { new ReportedRow(inventoryItemId, ItemDefinitionId) };
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    /// <summary>The row the card leaves; it stays.</summary>
    public long InventoryItemId { get; }

    public string ItemDefinitionId { get; }

    /// <summary>The card's slot, from 1; the cards after it move down a slot.</summary>
    public int Slot { get; }

    /// <summary>The card the client showed at that slot.</summary>
    public string CardDefinitionId { get; }

    public long Fee { get; }

    public int CardStackLimit { get; }

    public int MaxRows { get; }

    public DateTime At { get; }

    /// <summary>
    ///     The item's row, which the answer reports beside the card's bag row, the one the ledger row names (Persistence
    ///     §5).
    /// </summary>
    public IReadOnlyCollection<ReportedRow> ReportRows { get; }
}
}
