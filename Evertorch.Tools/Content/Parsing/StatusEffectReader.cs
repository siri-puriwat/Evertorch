using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class StatusEffectReader
{
    private const int MaxStatPercent = 1000;

    public static AuthoredStatusEffect? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        StatusDefinitionId id = root.RequiredId<StatusDefinitionId>(
            "id",
            StatusDefinitionId.TryCreate,
            StatusDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        YamlFieldReader percent = root.RequiredMapping("server").RequiredMapping("statPercent");
        var statPercent = new StatPercentages(
            OptionalPercent(percent, "str"),
            OptionalPercent(percent, "agi"),
            OptionalPercent(percent, "vit"),
            OptionalPercent(percent, "int"),
            OptionalPercent(percent, "dex"),
            OptionalPercent(percent, "luk"));

        string? icon = null;
        if (root.Has("client"))
        {
            YamlFieldReader client = root.RequiredMapping("client");
            icon = client.Has("icon") ? client.RequiredAssetKey("icon") : null;
        }

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new StatusEffectDefinition(id, displayName, statPercent);
        return new AuthoredStatusEffect(root.ToSource(), definition, icon);
    }

    private static int OptionalPercent(YamlFieldReader reader, string key)
    {
        return reader.Has(key) ? reader.RequiredInt(key, 0, MaxStatPercent) : 0;
    }
}
}
