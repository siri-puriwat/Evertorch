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

        // A converter's status sets the element of the wearer's swings and weapon skills, and an ailment's names the time
        // a worn card's landing lasts; no status is both (Gameplay Systems §9.1).
        Element? attackElement = null;
        Ailment ailment = Ailment.None;
        int durationMs = 0;
        if (root.Has("server"))
        {
            YamlFieldReader server = root.RequiredMapping("server");
            attackElement = server.Has("attackElement") ? server.RequiredEnum<Element>("attackElement") : null;
            if (server.Has("ailment"))
            {
                ailment = server.RequiredAilment("ailment");
                durationMs = server.RequiredInt("durationMs", 1, ContentLimits.MaxDurationMs);
                if (server.Has("attackElement"))
                {
                    server.ReportField("ailment", "is not for a status that sets an attack element");
                }
            }
            else if (server.Has("durationMs"))
            {
                server.ReportField("durationMs", "is only for an ailment's status");
            }
        }

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

        var definition = new StatusEffectDefinition(id, displayName, attackElement, ailment, durationMs);
        return new AuthoredStatusEffect(root.ToSource(), definition, icon);
    }
}
}
