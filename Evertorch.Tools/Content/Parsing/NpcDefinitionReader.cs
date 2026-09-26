using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class NpcDefinitionReader
{
    public static AuthoredNpc? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        NpcDefinitionId id = root.RequiredId<NpcDefinitionId>(
            "id",
            NpcDefinitionId.TryCreate,
            NpcDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");
        var shop = new List<ShopEntry>();
        if (root.Has("server"))
        {
            ReadShop(root.RequiredMapping("server"), diagnostics, shop);
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string prefab = client.RequiredAssetKey("prefab");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        return new AuthoredNpc(root.ToSource(), new NpcDefinition(id, displayName, shop), prefab);
    }

    // An NPC keeps a shop only when it sells something, so an authored shop lists at least one item, each once.
    private static void ReadShop(YamlFieldReader server, List<ContentDiagnostic> diagnostics, List<ShopEntry> shop)
    {
        int errorsBefore = diagnostics.Count;
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (YamlFieldReader entry in server.RequiredMappingSequence("shop"))
        {
            ItemDefinitionId item = entry.RequiredId<ItemDefinitionId>(
                "item",
                ItemDefinitionId.TryCreate,
                ItemDefinitionId.KindPrefix);
            int price = entry.RequiredInt("price", 1, ContentLimits.MaxPrice);
            if (item != default && !listed.Add(item.Value))
            {
                entry.ReportField("item", $"lists '{item.Value}' more than once");
            }

            shop.Add(new ShopEntry(item, price));
        }

        if (shop.Count == 0 && diagnostics.Count == errorsBefore)
        {
            server.ReportField("shop", "must list at least one item");
        }
    }
}
}
