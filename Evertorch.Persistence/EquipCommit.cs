using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     One equip to commit (Persistence §5): the row to wear in its destination, under a new operation ID. The rows to
///     report name what the server believes the destination holds, for an answer found in the ledger after a repeat.
/// </summary>
public sealed class EquipCommit
{
    public EquipCommit(
        Guid operationId,
        long characterId,
        long inventoryItemId,
        string slot,
        IReadOnlyCollection<ReportedRow> reportRows,
        DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        InventoryItemId = inventoryItemId;
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        ReportRows = reportRows ?? throw new ArgumentNullException(nameof(reportRows));
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    public long InventoryItemId { get; }

    /// <summary>The destination's stored name, one of the eight (Persistence §4).</summary>
    public string Slot { get; }

    public IReadOnlyCollection<ReportedRow> ReportRows { get; }

    public DateTime At { get; }
}
}
