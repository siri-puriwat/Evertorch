using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One card to insert (Persistence §5): the card's row and its item, and the row it goes into with the item the tick
///     thread saw and that item's slots, under a new operation ID. The store checks both rows again and appends the card
///     to the item row's cards.
/// </summary>
public sealed class InsertCardCommit
{
    public InsertCardCommit(
        Guid operationId,
        long characterId,
        long cardItemId,
        string cardDefinitionId,
        long inventoryItemId,
        string itemDefinitionId,
        int slots,
        DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        CardItemId = cardItemId;
        CardDefinitionId = cardDefinitionId ?? throw new ArgumentNullException(nameof(cardDefinitionId));
        InventoryItemId = inventoryItemId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Slots = slots;
        At = at;
        ReportRows = new[] { new ReportedRow(inventoryItemId, ItemDefinitionId) };
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    /// <summary>The card's row, which the ledger row names; one card leaves it, and it is deleted once emptied.</summary>
    public long CardItemId { get; }

    public string CardDefinitionId { get; }

    /// <summary>The row the card goes into.</summary>
    public long InventoryItemId { get; }

    public string ItemDefinitionId { get; }

    /// <summary>The item's slots, which its cards may not pass.</summary>
    public int Slots { get; }

    public DateTime At { get; }

    /// <summary>
    ///     The item's row, which the answer reports beside the card's (Persistence §5).
    /// </summary>
    public IReadOnlyCollection<ReportedRow> ReportRows { get; }
}
}
