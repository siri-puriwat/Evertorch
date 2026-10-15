using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A server-only test package for the eight positions (Coding Standards §10): the repository's content with a test
///     piece of each new category beside its own, so the server's equip, statistics, and inspection can be tested before
///     and apart from the authored pieces. The pieces sell to no shop and list on no board, so no NPC changes.
/// </summary>
internal static class GearPackage
{
    public const string Cap = "item.headgear.test_cap";
    public const string Buckler = "item.shield.test_buckler";
    public const string Cape = "item.garment.test_cape";
    public const string Boots = "item.footwear.test_boots";
    public const string Charm = "item.accessory.test_charm";
    public const string Bead = "item.accessory.test_bead";

    private const string Items = "items.json";

    /// <summary>The repository's package with the six test pieces, signed again with its client content version.</summary>
    public static Dictionary<string, byte[]> Build()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string clientContentVersion = PackageFixture.ClientContentVersionOf(files);
        // The root is reached through .Root: a local typed JsonNode would turn into var under cleanup (IDE0008).
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        definitions.Add(Piece(Cap, "Test Cap", "headgear", 2));
        definitions.Add(Piece(Buckler, "Test Buckler", "shield", 6, 5));
        definitions.Add(Piece(Cape, "Test Cape", "garment", 2));
        definitions.Add(Piece(Boots, "Test Boots", "footwear", 2));
        definitions.Add(Piece(Charm, "Test Charm", "accessory", 0, dex: 1));
        definitions.Add(Piece(Bead, "Test Bead", "accessory", 0, luk: 1));
        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files, clientContentVersion);
        return files;
    }

    private static JsonNode Piece(
        string id,
        string name,
        string type,
        int defense,
        int penalty = 0,
        int dex = 0,
        int luk = 0)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["type"] = type,
            ["stackLimit"] = 1,
            ["weight"] = 5,
            ["sellPrice"] = 0,
            ["slots"] = 1,
            ["equipment"] = new JsonObject
            {
                ["attack"] = 0,
                ["attackSpeedPenalty"] = penalty,
                ["defense"] = defense,
                ["bonus"] = new JsonObject
                {
                    ["str"] = 0,
                    ["agi"] = 0,
                    ["vit"] = 0,
                    ["int"] = 0,
                    ["dex"] = dex,
                    ["luk"] = luk
                }
            }
        };
    }
}
}
