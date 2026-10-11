using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader reads an item's terms on the market board and the NPC that keeps it, and refuses what the tools refuse
///     of them (Content Pipeline §7, §10). The fixture's Slime Gel sells for 2 and stacks to 999, so a unit of it may
///     cost at most 1,001,001.
/// </summary>
[TestFixture]
public sealed class ServerContentMarketTests
{
    private const string Items = "items.json";
    private const string Gel = "item.material.slime_gel";

    private const string Message =
        "must keep its floor from the sell price to the base, and its ceiling from the base to a stack's cap";

    private static IReadOnlyList<string> ProblemsOf(IReadOnlyDictionary<string, byte[]> files)
    {
        try
        {
            ServerContentLoader.Load(files);
        }
        catch (ContentLoadException exception)
        {
            return exception.Problems;
        }

        return Array.Empty<string>();
    }

    [TestCase("{\"basePrice\":4,\"floor\":1,\"ceiling\":40}")]
    [TestCase("{\"basePrice\":4,\"floor\":5,\"ceiling\":40}")]
    [TestCase("{\"basePrice\":4,\"floor\":2,\"ceiling\":3}")]
    [TestCase("{\"basePrice\":4,\"floor\":2,\"ceiling\":1001002}")]
    public void Load_ForMarketTermsOutOfBounds_RefusesTheItem(string market)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string path = PackageFixture.SetValue(files, Items, Gel, "market", market);

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{Items}: {path}: {Message}" }));
    }

    [TestCase("{\"basePrice\":4,\"floor\":2}", "market.ceiling: required property is missing")]
    [TestCase("{\"basePrice\":0,\"floor\":2,\"ceiling\":40}", "market.basePrice: must be at least 1")]
    [TestCase("{\"basePrice\":4,\"floor\":2,\"ceiling\":40,\"price\":3}", "market.price: unknown property")]
    public void Load_ForAMalformedMarket_ReportsExactlyThatField(string market, string expected)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string path = PackageFixture.SetValue(files, Items, Gel, "market", market);

        string entry = path.Substring(0, path.Length - ".market".Length);
        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { $"{Items}: {entry}.{expected}" }));
    }

    [Test]
    public void Load_ForAListableItemAndABroker_ReadsThem()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(files, Items, Gel, "market", "{\"basePrice\":4,\"floor\":2,\"ceiling\":1001001}");
        PackageFixture.SetValue(files, "npcs.json", "npc.quartermaster", "market", "true");

        ServerContent content = ServerContentLoader.Load(files);

        ItemMarket terms = content.Items[new ItemDefinitionId(Gel)].Market!;
        Assert.That((terms.BasePrice, terms.Floor, terms.Ceiling), Is.EqualTo((4, 2, 1_001_001)));
        Assert.That(content.Npcs[new NpcDefinitionId("npc.quartermaster")].KeepsMarket, Is.True);
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsTheNineListableGoodsAndTheBroker()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Items.Values.Where(item => item.Market != null)
                .Select(item => (item.Id.Value, item.Market!.BasePrice, item.Market.Floor, item.Market.Ceiling))
                .OrderBy(terms => terms.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.leather", 90, 45, 900), ("item.armor.monarch_mantle", 1000, 250, 10000),
                    ("item.material.emberstone", 40, 10, 400), ("item.material.gloom_dust", 28, 14, 280),
                    ("item.material.grotto_carapace", 24, 12, 240), ("item.material.monarch_jelly", 80, 40, 800),
                    ("item.material.wardstone", 40, 10, 400), ("item.weapon.ash_staff", 120, 60, 1200),
                    ("item.weapon.iron_sword", 120, 60, 1200)
                }));
        Assert.That(
            content.Npcs.Values.Where(npc => npc.KeepsMarket).Select(npc => (npc.Id.Value, npc.HasShop)),
            Is.EqualTo(new[] { ("npc.broker", false) }));
    }
}
}
