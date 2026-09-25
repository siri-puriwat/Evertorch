using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class ExperienceTableReader
{
    // A table of n levels caps its jobs at level n + 1, which must stay within the level limit.
    private const int MaxLevels = ContentLimits.MaxLevel - 1;

    public static AuthoredExperienceTable? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        ExperienceDefinitionId id = root.RequiredId<ExperienceDefinitionId>(
            "id",
            ExperienceDefinitionId.TryCreate,
            ExperienceDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        int errorsBeforeLevels = diagnostics.Count;
        IReadOnlyList<int> levels = root.RequiredIntSequence("levels", 1, ContentLimits.MaxExperience);
        if (diagnostics.Count == errorsBeforeLevels && (levels.Count == 0 || levels.Count > MaxLevels))
        {
            root.ReportField(
                "levels",
                string.Format(CultureInfo.InvariantCulture, "must list between 1 and {0} levels", MaxLevels));
        }

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new ExperienceTableDefinition(id, new List<int>(levels).AsReadOnly());
        return new AuthoredExperienceTable(root.ToSource(), definition);
    }
}
}
