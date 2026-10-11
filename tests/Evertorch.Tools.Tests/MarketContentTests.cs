using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Listable items and the Broker (Content Pipeline §4, §5, §7; Gameplay Systems §11.5): an item's terms on the
///     market board, their defaults and bounds, what each package carries, and the NPC that keeps the board. The
///     fixture's Slime Gel sells for 73,219 and stacks to 999, so a unit of it may cost at most 1,001,001.
/// </summary>
[TestFixture]
public sealed class MarketContentTests
{
    private const string Gel = "items/slime_gel.yml";
    private const string Npc = "npcs/quartermaster.yml";
    private const string SellPrice = "  sellPrice: 73219\n";

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

    [TestCase("    basePrice: 0\n", "server.market.basePrice")]
    [TestCase("    basePrice: 100000\n    floor: 73218\n", "server.market.floor")]
    [TestCase("    basePrice: 100000\n    floor: 100001\n", "server.market.floor")]
    [TestCase("    basePrice: 70000\n", "server.market.basePrice")]
    [TestCase("    basePrice: 100000\n    ceiling: 99999\n", "server.market.ceiling")]
    [TestCase("    basePrice: 100000\n    ceiling: 1001002\n", "server.market.ceiling")]
    [TestCase("    basePrice: 1001002\n", "server.market.basePrice")]
    [TestCase("    basePrice: 100000\n    price: 3\n", "server.market.price")]
    [TestCase("    floor: 80000\n", "server.market.basePrice")]
    public void Run_ForMarketTermsOutOfBounds_ReportsTheField(string market, string field)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Gel, SellPrice, $"{SellPrice}  market:\n{market}");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Has.Member((Gel, field)),
                Describe(result));
        }
    }

    [TestCase("    basePrice: 146438\n", 146438, 73219, 1001001)]
    [TestCase("    basePrice: 80000\n", 80000, 73219, 800000)]
    [TestCase("    basePrice: 200000\n    floor: 100000\n    ceiling: 300000\n", 200000, 100000, 300000)]
    public void Run_ForAListableItem_ResolvesItsTerms_AndOnlyTheServerPackageCarriesThem(
        string market,
        int basePrice,
        int floor,
        int ceiling)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Gel, SellPrice, $"{SellPrice}  market:\n{market}");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            ItemMarket terms = result.Content.Items
                .Single(item => item.Definition.Id.Value == "item.material.slime_gel")
                .Definition.Market!;
            Assert.That((terms.BasePrice, terms.Floor, terms.Ceiling), Is.EqualTo((basePrice, floor, ceiling)));
            JsonElement server = Definition(result.Packages!.Server, "items.json", "item.material.slime_gel")
                .GetProperty("market");
            Assert.That(
                (server.GetProperty("basePrice").GetInt32(), server.GetProperty("floor").GetInt32(),
                    server.GetProperty("ceiling").GetInt32()),
                Is.EqualTo((basePrice, floor, ceiling)));
            Assert.That(
                Definition(result.Packages.Client, "items.json", "item.material.slime_gel").TryGetProperty("market",
                    out JsonElement _),
                Is.False,
                "the board's terms are server-only");
        }
    }

    // A listable item whose stack limit is out of range has that reported, not a failure of the run.
    [Test]
    public void Run_ForAListableItemWithoutAStackLimit_ReportsTheStackLimit()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Gel, SellPrice, $"{SellPrice}  market:\n    basePrice: 100000\n");
            workspace.Replace(Gel, "stackLimit: 999\n", "stackLimit: 0\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
                Is.EqualTo(new[] { (Gel, "stackLimit") }),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForAnItemWithoutTerms_IsNotListed_AndTheServerPackageSaysNothing()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            Assert.That(result.Content.Items.Select(item => item.Definition.Market), Is.All.Null);
            Assert.That(
                Definition(result.Packages!.Server, "items.json", "item.material.slime_gel").TryGetProperty("market",
                    out JsonElement _),
                Is.False);
        }
    }

    [Test]
    public void Run_ForRepositoryContent_ListsTheGrottosNineGoods_AndTheBrokerKeepsTheBoard()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Market != null)
                .Select(item => (item.Definition.Id.Value, item.Definition.Market!.BasePrice,
                    item.Definition.Market.Floor, item.Definition.Market.Ceiling))
                .OrderBy(terms => terms.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.leather", 90, 45, 900), ("item.armor.monarch_mantle", 1000, 250, 10000),
                    ("item.material.emberstone", 40, 10, 400), ("item.material.gloom_dust", 28, 14, 280),
                    ("item.material.grotto_carapace", 24, 12, 240), ("item.material.monarch_jelly", 80, 40, 800),
                    ("item.material.wardstone", 40, 10, 400), ("item.weapon.ash_staff", 120, 60, 1200),
                    ("item.weapon.iron_sword", 120, 60, 1200)
                }),
            "the Umbral Grotto's goods at twice their sell price, the Mantle's and the ores' terms authored");
        AuthoredNpc broker = result.Content.Npcs.Single(npc => npc.Definition.KeepsMarket);
        Assert.That(
            (broker.Definition.Id.Value, broker.Definition.DisplayName, broker.Definition.HasShop, broker.Prefab,
                broker.Tint),
            Is.EqualTo(("npc.broker", "Broker", false, "npc_guildmaster", "#B8862B")),
            "the Guildmaster's body in a colour of its own");
        NpcPlacement placed = result.Content.Maps.Single(map => map.Definition.Id.Value == "map.training_ground")
            .Definition.Npcs.Single(npc => npc.Npc.Value == "npc.broker");
        Assert.That((placed.Position.X, placed.Position.Z), Is.EqualTo((3.5f, 4.5f)));
    }

    [Test]
    public void Run_WhenAnNpcKeepsTheBoardAlone_IsValid_AndTheServerPackageSaysSo()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(
                Npc,
                "  shop:\n    - item: item.material.slime_gel\n      price: 146443\n",
                "  market: true\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            NpcDefinition npc = result.Content.Npcs.Single().Definition;
            Assert.That((npc.HasShop, npc.KeepsMarket), Is.EqualTo((false, true)));
            Assert.That(
                Definition(result.Packages!.Server, "npcs.json", "npc.quartermaster").GetProperty("market")
                    .GetBoolean(),
                Is.True);
            Assert.That(
                Definition(result.Packages.Client, "npcs.json", "npc.quartermaster").TryGetProperty("market",
                    out JsonElement _),
                Is.False);
        }
    }
}
}
