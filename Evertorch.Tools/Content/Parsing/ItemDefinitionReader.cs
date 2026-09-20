using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class ItemDefinitionReader
{
    public static AuthoredItem? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        ItemDefinitionId id = root.RequiredId<ItemDefinitionId>(
            "id",
            ItemDefinitionId.TryCreate,
            ItemDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        ItemType type = root.RequiredEnum<ItemType>("type");
        int stackLimit = root.RequiredInt("stackLimit", 1, ContentLimits.MaxStack);

        YamlFieldReader server = root.RequiredMapping("server");
        int weight = server.RequiredInt("weight", 0, ContentLimits.MaxStack);
        int sellPrice = server.RequiredInt("sellPrice", 0, ContentLimits.MaxPrice);

        YamlFieldReader client = root.RequiredMapping("client");
        string icon = client.RequiredAssetKey("icon");
        string model = client.RequiredAssetKey("model");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        ItemDefinition definition = new ItemDefinition(id, displayName, type, stackLimit, weight, sellPrice);
        return new AuthoredItem(root.ToSource(), definition, icon, model);
    }
}
}
