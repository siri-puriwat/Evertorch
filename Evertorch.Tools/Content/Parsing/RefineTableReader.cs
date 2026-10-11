using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     Reads a refine table (Content Pipeline §4): its ore and how many an attempt takes, what a failure does, and for
///     each level from +1 the chance of reaching it, its coins, and its cumulative bonus. Server-only.
/// </summary>
internal static class RefineTableReader
{
    public static AuthoredRefineTable? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        RefineDefinitionId id = root.RequiredId<RefineDefinitionId>(
            "id",
            RefineDefinitionId.TryCreate,
            RefineDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        ItemDefinitionId material = root.RequiredId<ItemDefinitionId>(
            "material",
            ItemDefinitionId.TryCreate,
            ItemDefinitionId.KindPrefix);
        int materialQuantity = root.Has("materialQuantity")
            ? root.RequiredInt("materialQuantity", 1, ContentLimits.MaxRefineMaterialQuantity)
            : 1;
        RefineFailure onFailure = root.Has("onFailure")
            ? root.RequiredEnum<RefineFailure>("onFailure")
            : RefineFailure.Destroy;
        int downgradeBy = 0;
        if (onFailure == RefineFailure.Downgrade)
        {
            downgradeBy = root.Has("downgradeBy")
                ? root.RequiredInt("downgradeBy", 1, ContentLimits.MaxRefineDowngrade)
                : 1;
        }
        else if (root.Has("downgradeBy"))
        {
            root.ReportField("downgradeBy", "is only for a table whose onFailure is downgrade");
        }

        List<RefineStep> steps = ReadSteps(root, diagnostics);
        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new RefineTableDefinition(
            id,
            material,
            materialQuantity,
            onFailure,
            downgradeBy,
            steps.AsReadOnly());
        return new AuthoredRefineTable(root.ToSource(), definition);
    }

    // One to ten levels, each a chance from 1 to 100 %, coins, and a bonus that never falls below the level before's.
    private static List<RefineStep> ReadSteps(YamlFieldReader root, List<ContentDiagnostic> diagnostics)
    {
        int errorsBefore = diagnostics.Count;
        IReadOnlyList<YamlFieldReader> levels = root.RequiredMappingSequence("levels");
        var steps = new List<RefineStep>(levels.Count);
        if (diagnostics.Count == errorsBefore && (levels.Count == 0 || levels.Count > ContentLimits.MaxRefineLevels))
        {
            root.ReportField(
                "levels",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "must list between 1 and {0} levels",
                    ContentLimits.MaxRefineLevels));
        }

        int previousBonus = 0;
        foreach (YamlFieldReader level in levels)
        {
            int chance = level.RequiredInt("chance", 1, ContentLimits.MaxRefineChance);
            int coins = level.RequiredInt("coins", 0, ContentLimits.MaxRefineCoins);
            int bonus = level.RequiredInt("bonus", 0, ContentLimits.MaxRefineBonus);
            if (bonus < previousBonus)
            {
                level.ReportField("bonus", "must not fall below the level before's");
            }

            previousBonus = bonus;
            steps.Add(new RefineStep(chance, coins, bonus));
        }

        return steps;
    }
}
}
