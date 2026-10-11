using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A row an operation's answer reports beside the one its ledger row names, with the item it comes back under once
///     the operation emptied it (Persistence §5): a swap's displaced row, which is never emptied, or a refine's ore.
/// </summary>
public sealed class ReportedRow
{
    public ReportedRow(long id, string itemDefinitionId)
    {
        Id = id;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
    }

    public long Id { get; }

    public string ItemDefinitionId { get; }
}
}
