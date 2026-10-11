using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One refine, append-only like the ledger (Persistence §4): its operation, the character, the row refined and its
///     item, the levels before and after, the outcome, the ore's row, item, and quantity, and the coins. The rows are kept
///     as numbers, since a destroyed or an emptied row is deleted; the level after is null when the row was destroyed.
/// </summary>
internal sealed class RefineAttemptRow
{
    public const string RefinedOutcome = "refined";
    public const string DestroyedOutcome = "destroyed";
    public const string LoweredOutcome = "lowered";
    public const string UnchangedOutcome = "unchanged";

    public Guid OperationId { get; set; }

    public long CharacterId { get; set; }

    public long ItemInstanceId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int LevelBefore { get; set; }

    public int? LevelAfter { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public long MaterialInstanceId { get; set; }

    public string MaterialDefinitionId { get; set; } = string.Empty;

    public int MaterialQuantity { get; set; }

    public long Coins { get; set; }

    public DateTime CreatedAt { get; set; }
}
}
