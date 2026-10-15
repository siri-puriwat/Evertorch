using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The five categories of equipment beyond the weapon and armor (Content Pipeline §4; Gameplay Systems §11.1): a
///     headgear, a shield, a garment, footwear, and an accessory, each a stack limit of 1 and one slot at most, with an
///     optional defense; a shield's attack-speed penalty; no table on an accessory; and cards that fit any of them.
/// </summary>
[TestFixture]
public sealed class WornCategoryContentTests
{
    private const string Cap = "items/trail_cap.yml";
    private const string Buckler = "items/wooden_buckler.yml";
    private const string Charm = "items/traveler_charm.yml";
    private const string Card = "items/insight_card.yml";

    private const string CapText =
        "id: item.headgear.trail_cap\n"
        + "displayName: Trail Cap\n"
        + "type: headgear\n"
        + "stackLimit: 1\n"
        + "slots: 1\n"
        + "server:\n"
        + "  weight: 20\n"
        + "  sellPrice: 15\n"
        + "  equipment:\n"
        + "    defense: 2\n"
        + "client:\n"
        + "  icon: item_cloth_armor\n"
        + "  model: pickup_cloth_armor\n";

    private const string BucklerText =
        "id: item.shield.wooden_buckler\n"
        + "displayName: Wooden Buckler\n"
        + "type: shield\n"
        + "stackLimit: 1\n"
        + "slots: 1\n"
        + "server:\n"
        + "  weight: 40\n"
        + "  sellPrice: 25\n"
        + "  equipment:\n"
        + "    defense: 6\n"
        + "    attackSpeedPenalty: 5\n"
        + "client:\n"
        + "  icon: item_cloth_armor\n"
        + "  model: pickup_cloth_armor\n";

    private const string CharmText =
        "id: item.accessory.traveler_charm\n"
        + "displayName: Traveler Charm\n"
        + "type: accessory\n"
        + "stackLimit: 1\n"
        + "slots: 1\n"
        + "server:\n"
        + "  weight: 5\n"
        + "  sellPrice: 20\n"
        + "  equipment:\n"
        + "    bonus: { dex: 1 }\n"
        + "client:\n"
        + "  icon: item_cloth_armor\n"
        + "  model: pickup_cloth_armor\n";

    private const string CardText =
        "id: item.card.spark_wisp_insight\n"
        + "displayName: Spark Wisp Insight Card\n"
        + "type: card\n"
        + "stackLimit: 999\n"
        + "fits: headgear\n"
        + "server:\n"
        + "  weight: 1\n"
        + "  sellPrice: 0\n"
        + "  card:\n"
        + "    bonus: { int: 1 }\n"
        + "    maxSp: 15\n"
        + "    resist: { blind: 10 }\n"
        + "    removalFee: 200\n"
        + "client:\n"
        + "  icon: item_slime_gel\n"
        + "  model: pickup_slime_gel\n"
        + "  prefix: Insight\n"
        + "  description: INT +1, max SP +15, and blind resistance +10%.\n";

    private static ContentPipelineResult Build(string? file = null, string oldText = "", string newText = "")
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write(Cap, CapText);
            workspace.Write(Buckler, BucklerText);
            workspace.Write(Charm, CharmText);
            workspace.Write(Card, CardText);
            if (file != null)
            {
                workspace.Replace(file, oldText, newText);
            }

            return ContentPipeline.Run(workspace.ContentRoot);
        }
    }

    private static JsonElement Item(ContentPackage package, string id)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == "items.json");
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")
                .EnumerateArray()
                .Single(definition => definition.GetProperty("id").GetString() == id)
                .Clone();
        }
    }

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [TestCase(Cap, "slots: 1", "slots: 2", "slots", "between 0 and 1")]
    [TestCase(Cap, "stackLimit: 1", "stackLimit: 2", "stackLimit", "must be 1 for equipment")]
    [TestCase(Cap, "    defense: 2\n", "    defense: 2\n    attackSpeedPenalty: 5\n",
        "server.equipment.attackSpeedPenalty", "is only for a weapon or a shield")]
    [TestCase(Cap, "    defense: 2\n", "    defense: 2\n    attack: 5\n", "server.equipment.attack", "unknown field")]
    [TestCase(Cap, "  model: pickup_cloth_armor\n", "  model: pickup_cloth_armor\n  held: weapon_training_sword\n",
        "client.held", "is only for a weapon")]
    [TestCase(Buckler, "attackSpeedPenalty: 5", "attackSpeedPenalty: 201", "server.equipment.attackSpeedPenalty",
        "between 0 and 200")]
    [TestCase(Charm, "  sellPrice: 20\n", "  sellPrice: 20\n  refine: refine.light_armor\n", "server.refine",
        "is not for an accessory")]
    [TestCase(Card, "fits: headgear", "fits: material", "fits",
        "must be weapon, armor, headgear, shield, garment, footwear, or accessory")]
    [TestCase(Card, "    removalFee: 200\n", "    armorElement: earth\n    removalFee: 200\n",
        "server.card.armorElement", "is only for a card that fits armor")]
    [TestCase(Card, "    removalFee: 200\n", "    inflict: { poison: 0.05 }\n    removalFee: 200\n",
        "server.card.inflict", "is only for a card that fits a weapon")]
    public void Run_WhenAPieceOrItsCardIsBroken_ReportsThatField(
        string file,
        string oldText,
        string newText,
        string field,
        string message)
    {
        ContentPipelineResult result = Build(file, oldText, newText);

        Assert.That(
            result.Diagnostics.Any(diagnostic =>
                diagnostic.File == file && diagnostic.FieldPath == field && diagnostic.Message.Contains(message)),
            Is.True,
            Describe(result));
    }

    [Test]
    public void Build_WithAPieceOfEachNewKind_WritesItsCategoryAndSlotForBoth_AndItsValuesForTheServerAlone()
    {
        ContentPipelineResult result = Build();

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        JsonElement cap = Item(result.Packages!.Server, "item.headgear.trail_cap");
        JsonElement buckler = Item(result.Packages.Server, "item.shield.wooden_buckler");
        JsonElement charm = Item(result.Packages.Server, "item.accessory.traveler_charm");
        Assert.That(
            (cap.GetProperty("type").GetString(), cap.GetProperty("slots").GetInt32(),
                cap.GetProperty("equipment").GetProperty("defense").GetInt32()),
            Is.EqualTo(("headgear", 1, 2)));
        Assert.That(
            (buckler.GetProperty("equipment").GetProperty("defense").GetInt32(),
                buckler.GetProperty("equipment").GetProperty("attackSpeedPenalty").GetInt32()),
            Is.EqualTo((6, 5)),
            "a shield's penalty");
        Assert.That(
            (charm.GetProperty("equipment").GetProperty("defense").GetInt32(),
                charm.GetProperty("equipment").GetProperty("bonus").GetProperty("dex").GetInt32()),
            Is.EqualTo((0, 1)),
            "an accessory's defense is 0 when absent");
        JsonElement clientCap = Item(result.Packages.Client, "item.headgear.trail_cap");
        Assert.That(
            (clientCap.GetProperty("type").GetString(), clientCap.GetProperty("slots").GetInt32()),
            Is.EqualTo(("headgear", 1)));
        Assert.That(clientCap.TryGetProperty("equipment", out _), Is.False, "the values stay the server's");
        Assert.That(
            Item(result.Packages.Client, "item.card.spark_wisp_insight").GetProperty("fits").GetString(),
            Is.EqualTo("headgear"),
            "a card's fit for both packages");
        Assert.That(
            Item(result.Packages.Server, "item.card.spark_wisp_insight").GetProperty("card").GetProperty("resist")
                .GetProperty("blind").GetInt32(),
            Is.EqualTo(10),
            "a resistance on a card that fits no weapon");
    }
}
}
