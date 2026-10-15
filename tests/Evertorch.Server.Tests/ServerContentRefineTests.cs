using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader reads the refine tables and each item's table, and refuses what the tools refuse of them (Content
///     Pipeline §7, §10; Gameplay Systems §11.6). The repository's package holds three tables; an edit re-signs it.
/// </summary>
[TestFixture]
public sealed class ServerContentRefineTests
{
    private const string Refine = "refine.json";
    private const string Items = "items.json";
    private const string Armor = "refine.armor";

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

    [TestCase("materialQuantity", "0", "must be at least 1")]
    [TestCase("materialQuantity", "256", "must be at most 255")]
    [TestCase("onFailure", "\"melt\"", "unknown value 'melt'")]
    [TestCase("downgradeBy", "1", "must be from 1 for a downgrade, and 0 for any other failure")]
    [TestCase("levels", "[]", "must list between 1 and 10 levels")]
    [TestCase("levels[0].chance", "0", "must be at least 1")]
    [TestCase("levels[1].chance", "101", "must be at most 100")]
    [TestCase("levels[1].bonus", "4", "must not fall below the level before's")]
    [TestCase("levels[0].coins", "1000001", "must be at most 1000000")]
    [TestCase("price", "3", "unknown property")]
    public void Load_ForATableOutOfBounds_RefusesIt(string path, string json, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string reported = PackageFixture.SetValue(files, Refine, Armor, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.StartsWith($"{Refine}: {reported}").And.Contains(message),
            string.Join("\n", problems));
    }

    [TestCase("\"item.material.ore\"", "names unknown item 'item.material.ore' as its material")]
    [TestCase("\"item.weapon.iron_sword\"", "names 'item.weapon.iron_sword', which is not a material")]
    public void Load_ForATableWhoseOreIsUnknownOrNoMaterial_RefusesIt(string material, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Refine, Armor, "material", material);

        Assert.That(ProblemsOf(files), Has.Some.StartsWith($"{Refine}: {Armor}: {message}"));
    }

    [Test]
    public void Load_ForATableOfElevenLevels_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string levels = "[" + string.Join(
            ",",
            Enumerable.Range(1, 11).Select(level => $"{{\"chance\":50,\"coins\":1,\"bonus\":{level}}}")) + "]";
        PackageFixture.SetValue(files, Refine, Armor, "levels", levels);

        Assert.That(ProblemsOf(files), Has.Some.Contains("must list between 1 and 10 levels"));
    }

    [Test]
    public void Load_ForAnItemNamingAnUnknownTable_OrAMaterialNamingOne_RefusesThem()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Items, "item.weapon.iron_sword", "refine", "\"refine.axe\"");
        string gel = PackageFixture.SetValue(files, Items, "item.material.slime_gel", "refine", "\"refine.armor\"");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Member($"{Items}: item.weapon.iron_sword: names unknown refine table 'refine.axe'"));
        Assert.That(problems, Has.Member($"{Items}: {gel}: is only for equipment"));
    }

    [Test]
    public void Load_ForFourteenItemsOfOneTable_RefusesTheTable()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        JsonNode sword = definitions.Single(definition => (string?)definition!["id"] == "item.weapon.iron_sword")!;
        for (int index = 0; index < 12; index++)
        {
            JsonObject copy = JsonNode.Parse(sword.ToJsonString())!.AsObject();
            copy["id"] = $"item.weapon.copy_{index:00}";
            definitions.Add(copy);
        }

        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        Assert.That(
            ProblemsOf(files),
            Has.Member($"{Refine}: refine.weapon_dungeon: is named by more than the 13 items its message carries"),
            "the Ash Staff, the Iron Sword, and twelve copies");
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsTheFourTables_AndEveryPiecesTableButTheCharms()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        RefineTableDefinition armor = content.RefineTables[new RefineDefinitionId(Armor)];
        Assert.That(
            (armor.Material.Value, armor.MaterialQuantity, armor.OnFailure, armor.DowngradeBy, armor.LastLevel),
            Is.EqualTo(("item.material.wardstone", 1, RefineFailure.Destroy, 0, 10)));
        Assert.That(
            armor.Steps.Select(step => step.Bonus),
            Is.EqualTo(new[] { 5, 10, 15, 20, 28, 36, 44, 52, 62, 72 }));
        Assert.That(
            content.RefineTables.Keys.Select(id => id.Value).OrderBy(id => id, StringComparer.Ordinal),
            Is.EqualTo(new[] { Armor, "refine.light_armor", "refine.weapon_dungeon", "refine.weapon_training" }));
        Assert.That(
            content.Items.Values
                .Where(item => item.Refine.HasValue)
                .Select(item => item.Id.Value)
                .OrderBy(id => id, StringComparer.Ordinal),
            Is.EqualTo(
                content.Items.Values
                    .Where(item => item.IsEquipment && item.Type != ItemType.Accessory)
                    .Select(item => item.Id.Value)
                    .OrderBy(id => id, StringComparer.Ordinal)),
            "every piece but an accessory names a table");
    }
}
}
