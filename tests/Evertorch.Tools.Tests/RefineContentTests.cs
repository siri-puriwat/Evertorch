using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Refine tables and the items that name them (Content Pipeline §3, §4, §5, §7; Gameplay Systems §11.6): a table's
///     ore and quantity, what a failure does, its levels' chances, coins, and bonuses, their bounds, the item's table, the
///     server package that carries them, and the client package that carries none of it. The fixture's Slime Gel is the
///     ore, stacking to 999.
/// </summary>
[TestFixture]
public sealed class RefineContentTests
{
    private const string Table = "refine/sword.yml";
    private const string Sword = "items/training_sword.yml";
    private const string Npc = "npcs/quartermaster.yml";

    private const string TableText =
        "id: refine.sword\n"
        + "material: item.material.slime_gel\n"
        + "levels:\n"
        + "  - { chance: 100, coins: 20, bonus: 2 }\n"
        + "  - { chance: 60, coins: 20, bonus: 4 }\n";

    private const string SwordText =
        "id: item.weapon.training_sword\n"
        + "displayName: Training Sword\n"
        + "type: weapon\n"
        + "stackLimit: 1\n"
        + "server:\n"
        + "  weight: 50\n"
        + "  sellPrice: 25\n"
        + "  refine: refine.sword\n"
        + "  equipment:\n"
        + "    attack: 20\n"
        + "    attackSpeedPenalty: 50\n"
        + "    weaponType: sword\n"
        + "client:\n"
        + "  icon: item_training_sword\n"
        + "  model: pickup_training_sword\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentWorkspace Workspace()
    {
        var workspace = new ContentWorkspace();
        workspace.Write(Table, TableText);
        workspace.Write(Sword, SwordText);
        return workspace;
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

    [TestCase("material: item.material.slime_gel", "material: item.material.ore", "material")]
    [TestCase("material: item.material.slime_gel", "material: item.consumable.minor_health", "material")]
    [TestCase("material: item.material.slime_gel", "material: refine.sword", "material")]
    [TestCase("levels:", "materialQuantity: 0\nlevels:", "materialQuantity")]
    [TestCase("levels:", "materialQuantity: 256\nlevels:", "materialQuantity")]
    [TestCase("levels:", "onFailure: melt\nlevels:", "onFailure")]
    [TestCase("levels:", "downgradeBy: 2\nlevels:", "downgradeBy")]
    [TestCase("levels:", "onFailure: downgrade\ndowngradeBy: 0\nlevels:", "downgradeBy")]
    [TestCase("levels:", "onFailure: downgrade\ndowngradeBy: 10\nlevels:", "downgradeBy")]
    [TestCase("levels:", "price: 3\nlevels:", "price")]
    [TestCase("chance: 100,", "chance: 0,", "levels[0].chance")]
    [TestCase("chance: 60,", "chance: 101,", "levels[1].chance")]
    [TestCase("coins: 20, bonus: 2", "coins: 1000001, bonus: 2", "levels[0].coins")]
    [TestCase("bonus: 2", "bonus: 10000", "levels[0].bonus")]
    [TestCase("bonus: 4", "bonus: 1", "levels[1].bonus")]
    [TestCase("bonus: 4 }", "bonus: 4, safe: true }", "levels[1].safe")]
    public void Run_ForATableOutOfBounds_ReportsTheField(string oldText, string newText, string field)
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Table, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Has.Member((Table, field)),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForATableOfNoLevel_OrOfElevenLevels_ReportsTheLevels()
    {
        foreach (int count in new[] { 0, 11 })
        {
            using (ContentWorkspace workspace = Workspace())
            {
                string levels = count == 0
                    ? "levels: []\n"
                    : "levels:\n" + string.Concat(
                        Enumerable.Range(1, count)
                            .Select(level => $"  - {{ chance: 50, coins: 1, bonus: {level} }}\n"));
                workspace.Write(Table, "id: refine.sword\nmaterial: item.material.slime_gel\n" + levels);

                ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

                Assert.That(
                    result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                    Has.Member((Table, "levels")),
                    $"{count} levels: {Describe(result)}");
            }
        }
    }

    [Test]
    public void Run_ForATableOnATrainingSword_ResolvesItsDefaults_AndOnlyTheServerPackageCarriesIt()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            RefineTableDefinition table = result.Content.RefineTables.Single().Definition;
            Assert.That(
                (table.Id.Value, table.Material.Value, table.MaterialQuantity, table.OnFailure, table.DowngradeBy),
                Is.EqualTo(("refine.sword", "item.material.slime_gel", 1, RefineFailure.Destroy, 0)));
            Assert.That(
                table.Steps.Select(step => (step.Chance, step.Coins, step.Bonus)),
                Is.EqualTo(new[] { (100, 20, 2), (60, 20, 4) }));
            JsonElement written = Definition(result.Packages!.Server, "refine.json", "refine.sword");
            Assert.That(written.GetProperty("onFailure").GetString(), Is.EqualTo("destroy"));
            Assert.That(written.GetProperty("levels").GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                Definition(result.Packages!.Server, "items.json", "item.weapon.training_sword")
                    .GetProperty("refine")
                    .GetString(),
                Is.EqualTo("refine.sword"));
            Assert.That(
                result.Packages!.Client.DataFiles.Select(file => file.Path),
                Has.None.EqualTo("refine.json"),
                "no table reaches the client package");
            Assert.That(
                Definition(result.Packages!.Client, "items.json", "item.weapon.training_sword")
                    .TryGetProperty("refine", out JsonElement _),
                Is.False);
        }
    }

    [Test]
    public void Run_ForATableThatLowers_KeepsHowFar()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Table, "levels:", "onFailure: downgrade\ndowngradeBy: 2\nlevels:");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            RefineTableDefinition table = result.Content.RefineTables.Single().Definition;
            Assert.That((table.OnFailure, table.DowngradeBy), Is.EqualTo((RefineFailure.Downgrade, 2)));
        }
    }

    [Test]
    public void Run_ForAnItemNamingAnUnknownTable_OrATableOnAMaterial_ReportsTheItemsField()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Sword, "refine.sword", "refine.axe");
            workspace.Replace("items/slime_gel.yml", "  sellPrice: 73219\n",
                "  sellPrice: 73219\n  refine: refine.sword\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Is.SupersetOf(new[] { (Sword, "server.refine"), ("items/slime_gel.yml", "server.refine") }),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForAnOreAboveItsStackLimit_ReportsTheQuantity()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace("items/slime_gel.yml", "stackLimit: 999", "stackLimit: 3");
            workspace.Replace(Table, "levels:", "materialQuantity: 4\nlevels:");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Has.Member((Table, "materialQuantity")),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForFourteenItemsOfOneTable_ReportsTheFourteenth()
    {
        // The copies sell for nothing, so no shop trades them and only the table's room is passed.
        using (ContentWorkspace workspace = Workspace())
        {
            for (int index = 1; index <= 13; index++)
            {
                workspace.Write(
                    $"items/sword_{index:00}.yml",
                    SwordText.Replace("item.weapon.training_sword", $"item.weapon.sword_{index:00}",
                            StringComparison.Ordinal)
                        .Replace("sellPrice: 25", "sellPrice: 0", StringComparison.Ordinal));
            }

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Is.EqualTo(new[] { (Sword, "server.refine") }),
                "the training sword comes after sword_13 by ID, the fourteenth");
        }
    }

    // Prototype Content §6: the Smith refines alone, drawn with the Gate Warden's body in a colour of its own, and is
    // placed last, so only player and drop IDs move.
    [Test]
    public void Run_ForRepositoryContent_PlacesTheSmithLast_WithTheGateWardensBodyTinted()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        AuthoredNpc smith = result.Content.Npcs.Single(npc => npc.Definition.Refines);
        Assert.That(
            (smith.Definition.Id.Value, smith.Definition.DisplayName, smith.Definition.HasShop, smith.Prefab,
                smith.Tint),
            Is.EqualTo(("npc.smith", "Smith", false, "npc_gate_warden", "#A0522D")));
        NpcPlacement placed = result.Content.Maps.Single(map => map.Definition.Id.Value == "map.training_ground")
            .Definition.Npcs.Last();
        Assert.That((placed.Npc.Value, placed.Position.X, placed.Position.Z), Is.EqualTo(("npc.smith", -6.5f, 0.5f)));
    }

    [Test]
    public void Run_ForRepositoryContent_RefinesEveryWeaponAndArmor_ThroughThreeTables()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Type == ItemType.Weapon || item.Definition.Type == ItemType.Armor)
                .Select(item => (item.Definition.Id.Value, item.Definition.Refine?.Value))
                .OrderBy(pair => pair.Item1, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.cloth", "refine.armor"), ("item.armor.leather", "refine.armor"),
                    ("item.armor.monarch_mantle", "refine.armor"), ("item.weapon.ash_staff", "refine.weapon_dungeon"),
                    ("item.weapon.iron_sword", "refine.weapon_dungeon"),
                    ("item.weapon.training_staff", "refine.weapon_training"),
                    ("item.weapon.training_sword", "refine.weapon_training")
                }),
            "every weapon and armor, the Mantle on the armor's table");
        Assert.That(
            result.Content.RefineTables.Select(authored => authored.Definition).Select(table => (table.Id.Value,
                table.Material.Value, table.MaterialQuantity, table.OnFailure,
                string.Join(" ", table.Steps.Select(step => $"{step.Chance}/{step.Coins}/{step.Bonus}")))),
            Is.EqualTo(
                new[]
                {
                    ("refine.armor", "item.material.wardstone", 1, RefineFailure.Destroy,
                        "100/60/5 100/60/10 100/60/15 100/60/20 60/60/28 50/60/36 40/60/44 30/60/52 20/60/62 10/60/72"),
                    ("refine.weapon_dungeon", "item.material.emberstone", 1, RefineFailure.Destroy,
                        "100/100/3 100/100/6 100/100/9 100/100/12 100/100/15 100/100/18 60/100/21 40/100/24 30/100/27 "
                        + "20/100/30"),
                    ("refine.weapon_training", "item.material.emberstone", 1, RefineFailure.Destroy,
                        "100/20/2 100/20/4 100/20/6 100/20/8 100/20/10 100/20/12 100/20/14 60/20/16 40/20/18 20/20/20")
                }),
            "safe to +4, +6, and +7; the armor's defense scaled to the 4,000 divisor");
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Id.Value.EndsWith("stone", StringComparison.Ordinal))
                .Select(item => (item.Definition.Id.Value, item.Definition.DisplayName, item.Definition.Type,
                    item.Definition.StackLimit, item.Definition.SellPrice)),
            Is.EqualTo(
                new[]
                {
                    ("item.material.emberstone", "Emberstone", ItemType.Material, 999, 0),
                    ("item.material.wardstone", "Wardstone", ItemType.Material, 999, 0)
                }),
            "two ores no shop buys");
        MonsterDefinition wisp = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == "monster.gloom_wisp")
            .Definition;
        MonsterDrop ember = wisp.Drops[wisp.Drops.Count - 3];
        Assert.That(
            (ember.Item.Value, ember.Chance, ember.MinAmount, ember.MaxAmount),
            Is.EqualTo(("item.material.emberstone", 0.08, 1, 1)),
            "the wisp's Emberstone last but its card and its converter, so every earlier draw keeps its place");
    }

    [Test]
    public void Run_WhenAnNpcRefinesAlone_IsValid_AndTheServerPackageSaysSo()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(
                Npc,
                "  shop:\n    - item: item.material.slime_gel\n      price: 146443\n",
                "  refine: true\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            NpcDefinition npc = result.Content.Npcs.Single().Definition;
            Assert.That((npc.HasShop, npc.Refines), Is.EqualTo((false, true)));
            Assert.That(
                Definition(result.Packages!.Server, "npcs.json", "npc.quartermaster").GetProperty("refine")
                    .GetBoolean(),
                Is.True);
            Assert.That(
                Definition(result.Packages.Client, "npcs.json", "npc.quartermaster").TryGetProperty("refine",
                    out JsonElement _),
                Is.False);
        }
    }
}
}
