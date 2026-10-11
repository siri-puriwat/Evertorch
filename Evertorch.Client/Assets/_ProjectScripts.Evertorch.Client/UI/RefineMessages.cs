using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The Smith's words (Prototype Content §2): a row of its section with the next level's chance and cost, what a
///     failure does, the outcome of a refine, and the refusals of the command.
/// </summary>
public static class RefineMessages
{
    /// <summary>
    ///     "+6 Iron Sword → +7 · 40 % · 100 coins · Emberstone 1 (3 held)"
    /// </summary>
    public static string Row(ClientContent? content, InventoryEntry row, RefineTable table, uint held)
    {
        RefineTableStep step = table.Steps[row.RefineLevel];
        return $"{ShopMessages.ItemName(content, row.Item, row.RefineLevel)} → +{row.RefineLevel + 1}"
            + $" · {step.Chance} % · {ShopMessages.Coins(step.Coins)}"
            + $" · {ShopMessages.ItemName(content, table.Ore)} {table.OreQuantity} ({held} held)";
    }

    /// <summary>
    ///     "Press again to refine. On failure it is destroyed."
    /// </summary>
    public static string Prompt(RefineTable table)
    {
        return $"Press again to refine. {OnFailure(table)}";
    }

    /// <summary>
    ///     "On failure it is destroyed.", "On failure it falls 1 level.", or "On failure nothing changes."
    /// </summary>
    public static string OnFailure(RefineTable table)
    {
        return table.OnFailure switch
        {
            RefineFailure.Destroy => "On failure it is destroyed.",
            RefineFailure.Downgrade => table.LevelsLowered == 1
                ? "On failure it falls 1 level."
                : $"On failure it falls {table.LevelsLowered} levels.",
            _ => "On failure nothing changes."
        };
    }

    /// <summary>
    ///     "Refined Iron Sword to +7." or "The refine failed: +6 Iron Sword was destroyed.", "… fell to +5.", or "… is
    ///     unchanged."
    /// </summary>
    public static string Describe(RefineOutcome outcome, ClientContent? content)
    {
        string name = ShopMessages.ItemName(content, outcome.Item);
        string before = ShopMessages.ItemName(content, outcome.Item, outcome.LevelBefore);
        return outcome.Outcome switch
        {
            RefineOutcomeKind.Refined => $"Refined {name} to +{outcome.LevelAfter}.",
            RefineOutcomeKind.Destroyed => $"The refine failed: {before} was destroyed.",
            RefineOutcomeKind.Lowered => $"The refine failed: {before} fell to +{outcome.LevelAfter}.",
            _ => $"The refine failed: {before} is unchanged."
        };
    }

    /// <summary>
    ///     The words for a refused refine of <paramref name="request" />; any reason without its own words reads as any
    ///     refusal.
    /// </summary>
    public static string DescribeRefusal(
        RefineRequest request,
        RefineTable? table,
        CommandRejectionReason reason,
        ClientContent? content)
    {
        switch (reason)
        {
            case CommandRejectionReason.RequirementNotMet when table != null:
                return $"You need {table.OreQuantity} {ShopMessages.ItemName(content, table.Ore)}.";
            case CommandRejectionReason.NotEnoughCoins:
                return "Not enough coins.";
            case CommandRejectionReason.NotAllowedNow when table != null && request.Level >= table.Steps.Count:
                return $"It is already +{request.Level}.";
            case CommandRejectionReason.NotAllowedNow:
                return "That can't be refined.";
            default:
                return RejectionMessages.Describe(reason, false);
        }
    }
}
}
