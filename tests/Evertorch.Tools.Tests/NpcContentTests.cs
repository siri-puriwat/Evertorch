using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     NPCs, their shops, quests, and where maps place NPCs (Content Pipeline §4, §5, §7; Gameplay Systems §2.2,
///     §6.1, §11.3).
/// </summary>
[TestFixture]
public sealed class NpcContentTests
{
    private const string Npc = "npcs/quartermaster.yml";
    private const string Quest = "quests/slime_hunt.yml";
    private const string Map = "maps/training_ground.yml";
    private const string Placements = "  npcs:\n    - npc: npc.quartermaster\n";
    private const string MarkerRow = "\"#.............#...N....................#\"";

    private const string WardenText =
        "id: npc.warden\n"
        + "displayName: Warden\n"
        + "client:\n"
        + "  prefab: npc_warden\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static JsonElement Definition(ContentPackage package, string path, string id)
    {
        PackageFile file = package.DataFiles.Single(candidate => candidate.Path == path);
        using (var document = JsonDocument.Parse(file.Content))
        {
            return document.RootElement.GetProperty("definitions")
                .EnumerateArray()
                .Single(definition => definition.GetProperty("id").GetString() == id)
                .Clone();
        }
    }

    private static string[] PropertyNames(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToArray();
    }

    // Writes a material item that sells for 1, so any NPC with a shop also buys it.
    private static void AddSellableItem(ContentWorkspace workspace, int index)
    {
        workspace.Write(
            $"items/extra_{index:D2}.yml",
            $"id: item.material.extra_{index:D2}\ndisplayName: Extra\ntype: material\nstackLimit: 1\n"
            + "server:\n  weight: 1\n  sellPrice: 1\nclient:\n  icon: item_extra\n  model: pickup_extra\n");
    }

    // Walls the marker in, three metres or more from every side of it, so the places to stand beside it are there
    // but none can be walked to from the spawn point: columns 14 to 22 and rows 19 to 26, counted from the north.
    private static void WallTheMarkerIn(ContentWorkspace workspace)
    {
        string[] lines = workspace.Read(Map).Replace("\r\n", "\n").Split('\n');
        int first = Array.FindIndex(lines, line => line.Trim() == "rows:") + 1;
        for (int row = 19; row <= 26; row++)
        {
            string line = lines[first + row];
            int open = line.IndexOf('"', StringComparison.Ordinal) + 1;
            var cells = new StringBuilder(line.Substring(open, line.Length - open - 1));
            for (int column = 14; column <= 22; column++)
            {
                bool isEdge = row == 19 || row == 26 || column == 14 || column == 22;
                if (isEdge)
                {
                    cells[column] = '#';
                }
            }

            lines[first + row] = $"{line.Substring(0, open)}{cells}\"";
        }

        workspace.Write(Map, string.Join("\n", lines));
    }

    // With the quest it gives (146 bytes) and the header (12), the Quartermaster's services fit one message while it
    // trades at most 11 items of 74 bytes: 12 + 11 × 74 + 146 = 972, where 12 items take 1,046 of the 1,020.
    [TestCase(9, true)]
    [TestCase(10, false)]
    public void Run_ForAnNpcsServices_AcceptsWhatFitsOneMessage(int extraItems, bool isValid)
    {
        using (var workspace = new ContentWorkspace())
        {
            for (int index = 0; index < extraItems; index++)
            {
                AddSellableItem(workspace, index);
            }

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath, diagnostic.Message)),
                isValid
                    ? Is.Empty
                    : Is.EqualTo(
                        new[]
                        {
                            (Npc, "id", "offers services that take 1046 bytes, more than the 1020 one message carries")
                        }),
                Describe(result));
        }
    }

    [Test]
    public void Build_ForValidFixture_WritesTheShopQuestAndPlacementForTheServer_AndOnlyNamesForTheClient()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            ContentPackages packages = result.Packages!;
            JsonElement npc = Definition(packages.Server, "npcs.json", "npc.quartermaster");
            JsonElement entry = npc.GetProperty("shop").EnumerateArray().Single();
            Assert.That(
                (entry.GetProperty("item").GetString(), entry.GetProperty("price").GetInt32()),
                Is.EqualTo(("item.material.slime_gel", 146443)));
            JsonElement quest = Definition(packages.Server, "quests.json", "quest.slime_hunt");
            Assert.That(
                (quest.GetProperty("giver").GetString(), quest.GetProperty("monster").GetString(),
                    quest.GetProperty("count").GetInt32(), quest.GetProperty("baseExperience").GetInt32(),
                    quest.GetProperty("currency").GetInt32()),
                Is.EqualTo(("npc.quartermaster", "monster.training_slime", 7, 61933, 71129)));
            JsonElement placement = Definition(packages.Server, "maps.json", "map.training_ground")
                .GetProperty("npcs")
                .EnumerateArray()
                .Single();
            Assert.That(placement.GetProperty("npc").GetString(), Is.EqualTo("npc.quartermaster"));
            Assert.That(placement.GetProperty("position").GetProperty("z").GetDouble(), Is.EqualTo(-3.5));
            Assert.That(placement.GetProperty("facing").GetProperty("x").GetDouble(), Is.EqualTo(0.6875));

            Assert.That(
                PropertyNames(Definition(packages.Client, "npcs.json", "npc.quartermaster")),
                Is.EqualTo(new[] { "id", "displayName", "prefab" }));
            Assert.That(
                PropertyNames(Definition(packages.Client, "quests.json", "quest.slime_hunt")),
                Is.EqualTo(new[] { "id", "displayName" }));
            Assert.That(
                Definition(packages.Client, "maps.json", "map.training_ground").TryGetProperty("npcs", out _),
                Is.False);
        }
    }

    [Test]
    public void Run_ForRepositoryContent_PlacesBothNpcs_AndTheHuntIsTheGateWardens()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        AuthoredMap ground = result.Content.Maps.Single(map => map.Definition.Id.Value == "map.training_ground");
        Assert.That(
            ground.Definition.Npcs.Select(npc => (npc.Npc.Value, npc.Position.X, npc.Position.Z)),
            Is.EqualTo(new[] { ("npc.quartermaster", -3.5f, 4.5f), ("npc.gate_warden", 20.5f, 3.5f) }));
        AuthoredNpc quartermaster = result.Content.Npcs.Single(npc => npc.Definition.Id.Value == "npc.quartermaster");
        Assert.That(
            quartermaster.Definition.Shop.Select(entry => (entry.Item.Value, entry.Price)),
            Is.EquivalentTo(
                new[]
                {
                    ("item.consumable.minor_health", 20), ("item.consumable.minor_mana", 30),
                    ("item.armor.cloth", 40), ("item.weapon.training_sword", 50),
                    ("item.weapon.training_staff", 50)
                }));
        foreach (ShopEntry entry in quartermaster.Definition.Shop)
        {
            int sellPrice = result.Content.Items.Single(item => item.Definition.Id == entry.Item).Definition.SellPrice;
            Assert.That(entry.Price, Is.EqualTo(2 * sellPrice), $"{entry.Item}: twice its sell price");
        }

        QuestDefinition hunt = result.Content.Quests.Single().Definition;
        Assert.That(
            (hunt.Id.Value, hunt.Giver.Value, hunt.Monster.Value, hunt.Count, hunt.BaseExperience, hunt.Currency),
            Is.EqualTo(("quest.crawler_hunt", "npc.gate_warden", "monster.forest_crawler", 5, 150, 100)));
        Assert.That(
            result.Content.Npcs.Single(npc => npc.Definition.Id.Value == "npc.gate_warden").Definition.HasShop,
            Is.False);
    }

    [Test]
    public void Run_WhenAnNpcIsPlacedTwice_ReportsTheSecondPlacement()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Map, MarkerRow, "\"#.............#...N.N..................#\"");
            workspace.Replace(
                Map,
                Placements,
                Placements
                + "      position: { x: 0.5, y: 0.0, z: -3.5 }\n      facing: { x: 1.0, z: 0.0 }\n"
                + "    - npc: npc.quartermaster\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(
                    new[]
                    {
                        (Map, "server.npcs[1].npc",
                            "places NPC 'npc.quartermaster', which map.training_ground already places")
                    }),
                Describe(result));
        }
    }

    [Test]
    public void Run_WhenAnNpcKeepsNoShop_IsValid_AndBuysNothing()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(
                Npc,
                "server:\n  shop:\n    - item: item.material.slime_gel\n      price: 146443\n",
                string.Empty);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            Assert.That(result.Content.Npcs.Single().Definition.HasShop, Is.False);
            Assert.That(
                Definition(result.Packages!.Server, "npcs.json", "npc.quartermaster").GetProperty("shop")
                    .GetArrayLength(),
                Is.Zero);
        }
    }

    [Test]
    public void Run_WhenNoMapPlacesAQuestsGiver_ReportsTheQuest()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(
                Map,
                Placements + "      position: { x: -1.5, y: 0.0, z: -3.5 }\n      facing: { x: 0.6875, z: -0.3125 }\n",
                string.Empty);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(new[] { (Quest, "server.giver", "names NPC 'npc.quartermaster', which no map places") }),
                Describe(result));
        }
    }

    [Test]
    public void Run_WhenNoPlaceBesideTheMarkerCanBeReached_ReportsThePlacement()
    {
        using (var workspace = new ContentWorkspace())
        {
            WallTheMarkerIn(workspace);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(
                    new[]
                    {
                        ("server.npcs[0].position",
                            "has no place to stand within 3 m that can be reached from the spawn point")
                    }),
                Describe(result));
        }
    }

    [Test]
    public void Run_WhenTwoNpcsStandOnOneMarker_ReportsTheSecond()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write("npcs/warden.yml", WardenText);
            workspace.Replace(
                Map,
                Placements,
                Placements
                + "      position: { x: -1.5, y: 0.0, z: -3.5 }\n      facing: { x: 1.0, z: 0.0 }\n"
                + "    - npc: npc.warden\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(
                    new[] { ("server.npcs[1].position", "is on a marker cell where another NPC already stands") }),
                Describe(result));
        }
    }

    // A character keeps every quest it takes, and one quest log carries them all: the fifteenth by ID is refused.
    [Test]
    public void Run_WithMoreQuestsThanAQuestLogCarries_ReportsEachBeyondIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            for (int index = 0; index < 14; index++)
            {
                workspace.Write(
                    $"quests/extra_{index:D2}.yml",
                    $"id: quest.extra_{index:D2}\ndisplayName: Extra\nserver:\n  giver: npc.quartermaster\n"
                    + "  objective:\n    kill: { monster: monster.training_slime, count: 1 }\n"
                    + "  rewards:\n    baseExperience: 1\n    currency: 0\n");
            }

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics
                    .Where(diagnostic => diagnostic.Message.Contains("quests a quest log carries"))
                    .Select(diagnostic => (diagnostic.File, diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(new[] { (Quest, "id", "is one more than the 14 quests a quest log carries") }),
                Describe(result));
        }
    }
}
}
