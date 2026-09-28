using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class StatusEffectReader
{
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

        var definition = new StatusEffectDefinition(id, displayName);
        return new AuthoredStatusEffect(root.ToSource(), definition, icon);
    }
}
}
