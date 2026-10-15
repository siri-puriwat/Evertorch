using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A server-only test package for the eight positions (Coding Standards §10): the repository's content with a test
///     piece of each new category beside its own, so the server's equip, statistics, and inspection can be tested apart
///     from the authored pieces, and a ward card that passes the resistance cap when worn twice, the fourteenth card a
///     list of removal fees carries. The four light pieces name the armor's refine table. Nothing sells or lists them,
///     so no NPC changes.
/// </summary>
internal static class GearPackage
{
    public const string Cap = "item.headgear.test_cap";
    public const string Buckler = "item.shield.test_buckler";
    public const string Cape = "item.garment.test_cape";
    public const string Boots = "item.footwear.test_boots";
    public const string Charm = "item.accessory.test_charm";
    public const string Bead = "item.accessory.test_bead";
    public const string Ward = "item.card.test_ward";

    private const string Items = "items.json";
    private const string ArmorTable = "refine.armor";

    /// <summary>The repository's package with the test pieces and the ward, signed again with its client content version.</summary>
    public static Dictionary<string, byte[]> Build()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string clientContentVersion = PackageFixture.ClientContentVersionOf(files);
        // The root is reached through .Root: a local typed JsonNode would turn into var under cleanup (IDE0008).
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        definitions.Add(Piece(Cap, "Test Cap", "headgear", 2, table: ArmorTable));
        definitions.Add(Piece(Buckler, "Test Buckler", "shield", 6, 5, table: ArmorTable));
        definitions.Add(Piece(Cape, "Test Cape", "garment", 2, table: ArmorTable));
        definitions.Add(Piece(Boots, "Test Boots", "footwear", 2, table: ArmorTable));
        definitions.Add(Piece(Charm, "Test Charm", "accessory", 0, bonus: new PrimaryStats(0, 0, 0, 0, 1, 0)));
        definitions.Add(Piece(Bead, "Test Bead", "accessory", 0, bonus: new PrimaryStats(0, 0, 0, 0, 0, 1)));
        definitions.Add(WardCard());
        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files, clientContentVersion);
        return files;
    }

    private static JsonObject Stats(PrimaryStats stats)
    {
        return new JsonObject
        {
            ["str"] = stats.Str,
            ["agi"] = stats.Agi,
            ["vit"] = stats.Vit,
            ["int"] = stats.Int,
            ["dex"] = stats.Dex,
            ["luk"] = stats.Luk
        };
    }

    private static JsonNode Piece(
        string id,
        string name,
        string type,
        int defense,
        int penalty = 0,
        PrimaryStats bonus = default,
        string? table = null)
    {
        var piece = new JsonObject
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
                ["bonus"] = Stats(bonus)
            }
        };
        if (table != null)
        {
            piece["refine"] = table;
        }

        return piece;
    }

    // A card of every value the loader reads, 0 or empty but blind resistance +60 (Content Pipeline §5).
    private static JsonNode WardCard()
    {
        return new JsonObject
        {
            ["id"] = Ward,
            ["displayName"] = "Test Ward Card",
            ["type"] = "card",
            ["stackLimit"] = 999,
            ["weight"] = 1,
            ["sellPrice"] = 0,
            ["fits"] = "accessory",
            ["card"] = new JsonObject
            {
                ["bonus"] = Stats(default),
                ["attack"] = 0,
                ["magicAttack"] = 0,
                ["defense"] = 0,
                ["maxHp"] = 0,
                ["maxSp"] = 0,
                ["bossDamage"] = 0,
                ["bossDamageReduction"] = 0,
                ["raceDamage"] = new JsonObject(),
                ["elementDamage"] = new JsonObject(),
                ["sizeDamage"] = new JsonObject(),
                ["elementDamageReduction"] = new JsonObject(),
                ["armorElement"] = "neutral",
                ["inflict"] = new JsonObject(),
                ["resist"] = new JsonObject { ["blind"] = 60 },
                ["removalFee"] = 200
            }
        };
    }
}
}
