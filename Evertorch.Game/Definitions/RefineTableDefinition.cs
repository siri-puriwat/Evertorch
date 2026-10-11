using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     A refine table (Content Pipeline §4; Gameplay Systems §11.6): the ore an attempt takes and how many, what a failure
///     does, and the steps to each level from +1, so <see cref="Steps" />[i] reaches level i + 1. Server-only content;
///     the chances, the coins, the ore, and the failure reach the player on the wire.
/// </summary>
public sealed class RefineTableDefinition
{
    public RefineTableDefinition(
        RefineDefinitionId id,
        ItemDefinitionId material,
        int materialQuantity,
        RefineFailure onFailure,
        int downgradeBy,
        IReadOnlyList<RefineStep> steps)
    {
        Id = id;
        Material = material;
        MaterialQuantity = materialQuantity;
        OnFailure = onFailure;
        DowngradeBy = downgradeBy;
        Steps = steps;
    }

    public RefineDefinitionId Id { get; }

    public ItemDefinitionId Material { get; }

    public int MaterialQuantity { get; }

    public RefineFailure OnFailure { get; }

    /// <summary>The levels a failure lowers the item by; 0 unless <see cref="OnFailure" /> is a downgrade.</summary>
    public int DowngradeBy { get; }

    public IReadOnlyList<RefineStep> Steps { get; }

    /// <summary>The highest level the table refines to.</summary>
    public int LastLevel => Steps.Count;
}
}
