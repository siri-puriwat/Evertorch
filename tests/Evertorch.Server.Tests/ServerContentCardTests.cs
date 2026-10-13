using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The loader reads the cards and the slots, and refuses what the tools refuse of them (Content Pipeline §7;
///     Gameplay Systems §11.7). The repository's package holds six cards; an edit re-signs it.
/// </summary>
[TestFixture]
public sealed class ServerContentCardTests
{
    private const string Items = "items.json";
    private const string Keen = "item.card.forest_crawler";

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

    [TestCase(Keen, "card.attack", "10000", "must be at most 9999")]
    [TestCase(Keen, "card.bonus.luk", "10000", "must be at most 9999")]
    [TestCase(Keen, "card.bossDamage", "101", "must be at most 100")]
    [TestCase(Keen, "card.bossDamageReduction", "91", "must be at most 90")]
    [TestCase(Keen, "card.removalFee", "1000001", "must be at most 1000000")]
    [TestCase(Keen, "card.maxSp", "-1", "must be at least 0")]
    [TestCase(Keen, "card.price", "3", "unknown property")]
    [TestCase(Keen, "fits", "\"shield\"", "unknown value 'shield'")]
    [TestCase(Keen, "fits", "\"none\"", "must be weapon or armor")]
    [TestCase("item.weapon.iron_sword", "slots", "5", "must be at most 4")]
    [TestCase("item.armor.leather", "slots", "2", "must be at most 1")]
    [TestCase("item.material.slime_gel", "slots", "1", "is only for a weapon or armor")]
    [TestCase("item.material.slime_gel", "fits", "\"weapon\"", "is only for a card")]
    [TestCase("item.weapon.iron_sword", "card", "{}", "is only for a card")]
    public void Load_ForACardOrSlotsOutOfBounds_RefusesThem(string id, string path, string json, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string reported = PackageFixture.SetValue(files, Items, id, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.StartsWith($"{Items}: {reported}").And.Contains(message),
            string.Join("\n", problems));
    }

    private static PrimaryStats Stats(int vit = 0, int @int = 0)
    {
        return new PrimaryStats(0, 0, vit, @int, 0, 0);
    }

    // The review's finding (session task T18): a refiner in content without a card promises no removal, since no fees
    // would follow, and the repository's Smith promises it with its six fees.
    [Test]
    public void ARefiner_TakesCardsOut_OnlyWhenTheContentHoldsACard()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.SetValue(files, "npcs.json", "npc.quartermaster", "refine", "true");
        ServerContent fixture = ServerContentLoader.Load(files);
        ServerContent repository = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());
        NpcDefinition quartermaster = fixture.Npcs[new NpcDefinitionId("npc.quartermaster")];
        NpcDefinition smith = repository.Npcs[new NpcDefinitionId("npc.smith")];

        NpcServices withoutCards = NpcServicesBuilder.Build(new EntityId(1), quartermaster, fixture);
        NpcServices withCards = NpcServicesBuilder.Build(new EntityId(2), smith, repository);

        Assert.That(
            (withoutCards.Refines, withoutCards.RemovesCards,
                NpcServicesBuilder.BuildCardRemovalFees(new EntityId(1), quartermaster, fixture)),
            Is.EqualTo((true, false, (CardRemovalFees?)null)));
        Assert.That(
            (withCards.RemovesCards,
                NpcServicesBuilder.BuildCardRemovalFees(new EntityId(2), smith, repository)!.Entries.Count),
            Is.EqualTo((true, 6)));
    }

    [Test]
    public void Load_ForACardAddingNothing_OrMissingItsFit_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string card = PackageFixture.SetValue(files, Items, Keen, "card.attack", "0");
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        JsonObject wisp = definitions
            .Single(definition => (string?)definition!["id"] == "item.card.gloom_wisp")!
            .AsObject();
        wisp.Remove("fits");
        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        IReadOnlyList<string> problems = ProblemsOf(files);

        string definition = card.Substring(0, card.IndexOf('.', StringComparison.Ordinal));
        Assert.That(problems, Has.Member($"{Items}: {definition}.card: must add at least one value above 0"));
        Assert.That(problems, Has.Some.EndsWith(".fits: required property is missing"), string.Join("\n", problems));
    }

    [Test]
    public void Load_ForFifteenCards_RefusesTheItems()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        JsonNode keen = definitions.Single(definition => (string?)definition!["id"] == Keen)!;
        for (int index = 0; index < 9; index++)
        {
            JsonObject copy = JsonNode.Parse(keen.ToJsonString())!.AsObject();
            copy["id"] = $"item.card.copy_{index:00}";
            definitions.Add(copy);
        }

        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        Assert.That(
            ProblemsOf(files),
            Has.Member($"{Items}: holds 15 cards, more than the 14 a list of removal fees carries"));
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsTheSixCards_AndEveryWeaponAndArmorsSlots()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Items.Values
                .Where(item => item.Type == ItemType.Card)
                .Select(item => (item.Id.Value, item.Card!.Fits, item.Card.RemovalFee, item.Card.Bonus.Stats,
                    item.Card.Bonus.Attack, item.Card.Bonus.MagicAttack, item.Card.Bonus.Defense, item.Card.Bonus.MaxHp,
                    item.Card.Bonus.BossDamagePercent, item.Card.Bonus.BossDamageReductionPercent))
                .OrderBy(card => card.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    (Keen, EquipmentSlot.Weapon, 200, Stats(), 8, 0, 0, 0, 0, 0),
                    ("item.card.gloom_wisp", EquipmentSlot.Weapon, 600, Stats(), 0, 0, 0, 0, 20, 0),
                    ("item.card.grotto_crawler", EquipmentSlot.Armor, 600, Stats(), 0, 0, 20, 30, 0, 0),
                    ("item.card.slime_monarch", EquipmentSlot.Armor, 5000, Stats(), 0, 0, 0, 40, 0, 25),
                    ("item.card.spark_wisp", EquipmentSlot.Weapon, 200, Stats(@int: 1), 0, 10, 0, 0, 0, 0),
                    ("item.card.training_slime", EquipmentSlot.Armor, 200, Stats(1), 0, 0, 0, 20, 0, 0)
                }));
        Assert.That(
            content.Items.Values
                .Where(item => item.Slot != EquipmentSlot.None)
                .Select(item => (item.Id.Value, item.Slots))
                .OrderBy(item => item.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.cloth", 1), ("item.armor.leather", 1), ("item.armor.monarch_mantle", 1),
                    ("item.weapon.ash_staff", 2), ("item.weapon.iron_sword", 2), ("item.weapon.training_staff", 1),
                    ("item.weapon.training_sword", 1)
                }));
        Assert.That(
            content.Items.Values.Where(item => item.Type != ItemType.Card).Select(item => item.Card),
            Is.All.Null);
        Assert.That(
            content.Items.Values
                .Where(item => item.Type == ItemType.Card)
                .Select(item => (item.Id.Value, item.StackLimit, item.SellPrice, item.Market!.BasePrice,
                    item.Market.Floor, item.Market.Ceiling))
                .OrderBy(card => card.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    (Keen, 999, 0, 800, 80, 8000), ("item.card.gloom_wisp", 999, 0, 2400, 240, 24000),
                    ("item.card.grotto_crawler", 999, 0, 2400, 240, 24000),
                    ("item.card.slime_monarch", 999, 0, 24000, 2400, 240000),
                    ("item.card.spark_wisp", 999, 0, 800, 80, 8000),
                    ("item.card.training_slime", 999, 0, 800, 80, 8000)
                }),
            "every card on the board, none sold to a shop");
    }
}
}
