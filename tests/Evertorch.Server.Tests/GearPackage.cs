using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A server-only test package for the eight positions (Coding Standards §10): the repository's content with a test
///     piece of each new category and a test card for each beside its own, so the server's equip, statistics, and
///     inspection can be tested before and apart from the authored pieces. The four light pieces name the armor's
///     refine table; the cards mirror the five the plan authors, and a ward passes the resistance cap when worn twice.
///     Nothing sells or lists them, so no NPC changes.
/// </summary>
internal static class GearPackage
{
    public const string Cap = "item.headgear.test_cap";
    public const string Buckler = "item.shield.test_buckler";
    public const string Cape = "item.garment.test_cape";
    public const string Boots = "item.footwear.test_boots";
    public const string Charm = "item.accessory.test_charm";
    public const string Bead = "item.accessory.test_bead";
    public const string Insight = "item.card.test_insight";
    public const string Bulwark = "item.card.test_bulwark";
    public const string Veil = "item.card.test_veil";
    public const string Stride = "item.card.test_stride";
    public const string Poise = "item.card.test_poise";
    public const string Ward = "item.card.test_ward";

    private const string Items = "items.json";
    private const string ArmorTable = "refine.armor";

    /// <summary>The repository's package with the test pieces and cards, signed again with its client content version.</summary>
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
        definitions.Add(Card(Insight, "headgear", new PrimaryStats(0, 0, 0, 1, 0, 0), maxSp: 15));
        definitions.Add(Card(Bulwark, "shield", defense: 5, bossDamageReduction: 5));
        definitions.Add(Card(Veil, "garment", reduced: ("shadow", 10), resisted: ("blind", 10)));
        definitions.Add(Card(Stride, "footwear", new PrimaryStats(0, 1, 0, 0, 0, 0), maxHp: 10));
        definitions.Add(Card(Poise, "accessory", new PrimaryStats(0, 0, 0, 0, 1, 1)));
        definitions.Add(Card(Ward, "accessory", resisted: ("blind", 60)));
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

    // Every value the loader reads, 0 or empty unless given (Content Pipeline §5).
    private static JsonNode Card(
        string id,
        string fits,
        PrimaryStats stats = default,
        int defense = 0,
        int maxHp = 0,
        int maxSp = 0,
        int bossDamageReduction = 0,
        (string Element, int Percent)? reduced = null,
        (string Ailment, int Percent)? resisted = null)
    {
        var reduction = new JsonObject();
        if (reduced.HasValue)
        {
            reduction[reduced.Value.Element] = reduced.Value.Percent;
        }

        var resist = new JsonObject();
        if (resisted.HasValue)
        {
            resist[resisted.Value.Ailment] = resisted.Value.Percent;
        }

        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = id,
            ["type"] = "card",
            ["stackLimit"] = 999,
            ["weight"] = 1,
            ["sellPrice"] = 0,
            ["fits"] = fits,
            ["card"] = new JsonObject
            {
                ["bonus"] = Stats(stats),
                ["attack"] = 0,
                ["magicAttack"] = 0,
                ["defense"] = defense,
                ["maxHp"] = maxHp,
                ["maxSp"] = maxSp,
                ["bossDamage"] = 0,
                ["bossDamageReduction"] = bossDamageReduction,
                ["raceDamage"] = new JsonObject(),
                ["elementDamage"] = new JsonObject(),
                ["sizeDamage"] = new JsonObject(),
                ["elementDamageReduction"] = reduction,
                ["armorElement"] = "neutral",
                ["inflict"] = new JsonObject(),
                ["resist"] = resist,
                ["removalFee"] = 200
            }
        };
    }
}
}
