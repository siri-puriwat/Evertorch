using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class ExperienceTableReader
{
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
        bool isCountOutOfRange = levels.Count == 0 || levels.Count > ContentLimits.MaxExperienceLevels;
        if (diagnostics.Count == errorsBeforeLevels && isCountOutOfRange)
        {
            root.ReportField(
                "levels",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "must list between 1 and {0} levels",
                    ContentLimits.MaxExperienceLevels));
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
