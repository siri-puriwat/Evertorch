using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Cards and slots (Content Pipeline §4, §5, §7; Gameplay Systems §11.7): a card's fit, values, fee, prefix, and
///     description, their bounds and the description's check, the slots of a weapon and an armor, the cards' count, the
///     server package that carries every value, and the client package that carries the facts the player sees.
/// </summary>
[TestFixture]
public sealed class CardContentTests
{
    private const string Card = "items/keen_card.yml";
    private const string Sword = "items/training_sword.yml";
    private const string Armor = "items/cloth_armor.yml";
    private const string Gel = "items/slime_gel.yml";

    private const string CardText =
        "id: item.card.keen\n"
        + "displayName: Keen Card\n"
        + "type: card\n"
        + "stackLimit: 999\n"
        + "fits: weapon\n"
        + "server:\n"
        + "  weight: 1\n"
        + "  sellPrice: 0\n"
        + "  market:\n"
        + "    basePrice: 800\n"
        + "    floor: 80\n"
        + "  card:\n"
        + "    bonus:\n"
        + "      str: 2\n"
        + "    attack: 8\n"
        + "    bossDamage: 20\n"
        + "    removalFee: 200\n"
        + "client:\n"
        + "  icon: item_slime_gel\n"
        + "  model: pickup_slime_gel\n"
        + "  prefix: Keen\n"
        + "  description: STR +2, attack +8, and damage to bosses +20%.\n";

    private const string SwordText =
        "id: item.weapon.training_sword\n"
        + "displayName: Training Sword\n"
        + "type: weapon\n"
        + "stackLimit: 1\n"
        + "slots: 2\n"
        + "server:\n"
        + "  weight: 50\n"
        + "  sellPrice: 25\n"
        + "  equipment:\n"
        + "    attack: 20\n"
        + "    attackSpeedPenalty: 50\n"
        + "    weaponType: sword\n"
        + "client:\n"
        + "  icon: item_training_sword\n"
        + "  model: pickup_training_sword\n";

    private const string ArmorText =
        "id: item.armor.cloth\n"
        + "displayName: Cloth Armor\n"
        + "type: armor\n"
        + "stackLimit: 1\n"
        + "server:\n"
        + "  weight: 60\n"
        + "  sellPrice: 20\n"
        + "  equipment:\n"
        + "    defense: 8\n"
        + "client:\n"
        + "  icon: item_cloth_armor\n"
        + "  model: pickup_cloth_armor\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentWorkspace Workspace()
    {
        var workspace = new ContentWorkspace();
        workspace.Write(Card, CardText);
        workspace.Write(Sword, SwordText);
        workspace.Write(Armor, ArmorText);
        return workspace;
    }

    private static JsonElement Definition(ContentPackage package, string id)
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

    private static string[] NamesOf(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static (string File, string FieldPath)[] FieldsOf(ContentPipelineResult result)
    {
        return result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)).ToArray();
    }

    [TestCase("fits: weapon", "fits: shield", "fits")]
    [TestCase("fits: weapon\n", "", "fits")]
    [TestCase("    attack: 8", "    attack: 10000", "server.card.attack")]
    [TestCase("      str: 2", "      str: 10000", "server.card.bonus.str")]
    [TestCase("    bossDamage: 20", "    bossDamage: 101", "server.card.bossDamage")]
    [TestCase("    bossDamage: 20", "    bossDamage: 20\n    bossDamageReduction: 91",
        "server.card.bossDamageReduction")]
    [TestCase("    removalFee: 200", "    removalFee: 1000001", "server.card.removalFee")]
    [TestCase("    removalFee: 200\n", "", "server.card.removalFee")]
    [TestCase("    removalFee: 200", "    removalFee: 200\n    price: 3", "server.card.price")]
    [TestCase("  card:\n", "  cards:\n", "server.card")]
    [TestCase("  prefix: Keen", "  prefix: keen", "client.prefix")]
    [TestCase("  prefix: Keen", "  prefix: Keenestly", "client.prefix")]
    [TestCase("  prefix: Keen", "  prefix: Keen2", "client.prefix")]
    [TestCase("  prefix: Keen\n", "", "client.prefix")]
    [TestCase("STR +2, attack", "Attack", "client.description")]
    [TestCase("bosses +20%", "bosses +2%", "client.description")]
    [TestCase("attack +8", "attack +80", "client.description")]
    [TestCase("  description: STR +2, attack +8, and damage to bosses +20%.\n", "", "client.description")]
    public void Run_ForACardOutOfBounds_ReportsTheField(string oldText, string newText, string field)
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Card, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(FieldsOf(result), Has.Member((Card, field)), Describe(result));
        }
    }

    [TestCase(Sword, "slots: 2", "slots: 5")]
    [TestCase(Sword, "slots: 2", "slots: -1")]
    [TestCase(Armor, "stackLimit: 1\n", "stackLimit: 1\nslots: 2\n")]
    public void Run_ForSlotsOutOfBounds_ReportsThem(string file, string oldText, string newText)
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(file, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(FieldsOf(result), Has.Member((file, "slots")), Describe(result));
        }
    }

    // The values above 0, named.
    private static string Bonus(CardBonus bonus)
    {
        (string Name, int Value)[] values =
        {
            ("str", bonus.Stats.Str), ("agi", bonus.Stats.Agi), ("vit", bonus.Stats.Vit), ("int", bonus.Stats.Int),
            ("dex", bonus.Stats.Dex), ("luk", bonus.Stats.Luk), ("attack", bonus.Attack),
            ("magicAttack", bonus.MagicAttack), ("defense", bonus.Defense), ("maxHp", bonus.MaxHp),
            ("maxSp", bonus.MaxSp), ("boss", bonus.BossDamagePercent), ("reduction", bonus.BossDamageReductionPercent)
        };
        return string.Join(" ", values.Where(value => value.Value > 0).Select(value => $"{value.Name} {value.Value}"));
    }

    [Test]
    public void Run_ForACardAddingNothing_OrALongDescription_ReportsEach()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Card, "    bonus:\n      str: 2\n    attack: 8\n    bossDamage: 20\n", string.Empty);
            workspace.Replace(
                Card,
                "and damage to bosses +20%.",
                "and damage to bosses +20%." + new string('!', 200));

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                FieldsOf(result),
                Is.SupersetOf(new[] { (Card, "server.card.bonus"), (Card, "client.description") }),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForACardAndASlottedSword_ReadsThem_AndEachPackageCarriesItsShare()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            AuthoredItem authored = result.Content.Items.Single(item => item.Definition.Type == ItemType.Card);
            ItemCard card = authored.Definition.Card!;
            Assert.That(
                (card.Fits, card.Bonus.Stats, card.Bonus.Attack, card.Bonus.BossDamagePercent, card.RemovalFee),
                Is.EqualTo((EquipmentSlot.Weapon, new PrimaryStats(2, 0, 0, 0, 0, 0), 8, 20, 200)));
            Assert.That(
                (authored.Prefix, authored.Description),
                Is.EqualTo(("Keen", "STR +2, attack +8, and damage to bosses +20%.")));
            Assert.That(
                result.Content.Items.Select(item => (item.Definition.Id.Value, item.Definition.Slots)),
                Is.SupersetOf(new[] { ("item.weapon.training_sword", 2), ("item.armor.cloth", 0) }),
                "an absent count is none");

            JsonElement server = Definition(result.Packages!.Server, "item.card.keen");
            Assert.That(server.GetProperty("fits").GetString(), Is.EqualTo("weapon"));
            JsonElement values = server.GetProperty("card");
            Assert.That(
                NamesOf(values),
                Is.EqualTo(
                    new[]
                    {
                        "bonus", "attack", "magicAttack", "defense", "maxHp", "maxSp", "bossDamage",
                        "bossDamageReduction", "removalFee"
                    }),
                "every value, 0 when absent");
            Assert.That(
                (values.GetProperty("bonus").GetProperty("str").GetInt32(), values.GetProperty("maxHp").GetInt32()),
                Is.EqualTo((2, 0)));
            Assert.That(
                Definition(result.Packages.Server, "item.armor.cloth").GetProperty("slots").GetInt32(),
                Is.Zero);

            JsonElement client = Definition(result.Packages.Client, "item.card.keen");
            Assert.That(
                NamesOf(client),
                Is.EqualTo(
                    new[]
                    {
                        "id", "displayName", "type", "stackLimit", "icon", "model", "fits", "prefix", "description"
                    }));
            Assert.That(
                (client.GetProperty("type").GetString(), client.GetProperty("fits").GetString(),
                    client.GetProperty("prefix").GetString()),
                Is.EqualTo(("card", "weapon", "Keen")));
            Assert.That(
                Definition(result.Packages.Client, "item.weapon.training_sword").GetProperty("slots").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                Definition(result.Packages.Client, "item.material.slime_gel")
                    .TryGetProperty("slots", out JsonElement _),
                Is.False,
                "slots belong to a weapon or an armor");
        }
    }

    [Test]
    public void Run_ForACardsFieldsOnAnotherItem_ReportsEach()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            workspace.Replace(Gel, "stackLimit: 999\n", "stackLimit: 999\nfits: weapon\nslots: 1\n");
            workspace.Replace(Gel, "  sellPrice: 73219\n", "  sellPrice: 73219\n  card:\n    attack: 8\n");
            workspace.Replace(Gel, "client:\n", "client:\n  prefix: Gooey\n  description: Gooey.\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                FieldsOf(result),
                Is.SupersetOf(
                    new[]
                    {
                        (Gel, "fits"), (Gel, "slots"), (Gel, "server.card"), (Gel, "client.prefix"),
                        (Gel, "client.description")
                    }),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForFifteenCards_ReportsOneTooMany()
    {
        using (ContentWorkspace workspace = Workspace())
        {
            for (int index = 0; index < 14; index++)
            {
                workspace.Write(
                    $"items/copy_{index:00}_card.yml",
                    CardText.Replace("item.card.keen", $"item.card.copy_{index:00}"));
            }

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.FieldPath, diagnostic.Message)),
                Is.EqualTo(new[] { ("type", "is one more than the 14 cards a list of removal fees carries") }),
                Describe(result));
        }
    }

    [Test]
    public void Run_ForRepositoryContent_ReadsTheSixCards_TheirDropsLast_AndTheSlots()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Type == ItemType.Card)
                .Select(item => item.Definition)
                .Select(item => (item.Id.Value, item.Card!.Fits, item.StackLimit, item.Weight, item.SellPrice,
                    item.Card.RemovalFee, item.Market!.BasePrice, item.Market.Floor, item.Market.Ceiling))
                .OrderBy(card => card.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.card.forest_crawler", EquipmentSlot.Weapon, 999, 1, 0, 200, 800, 80, 8000),
                    ("item.card.gloom_wisp", EquipmentSlot.Weapon, 999, 1, 0, 600, 2400, 240, 24000),
                    ("item.card.grotto_crawler", EquipmentSlot.Armor, 999, 1, 0, 600, 2400, 240, 24000),
                    ("item.card.slime_monarch", EquipmentSlot.Armor, 999, 1, 0, 5000, 24000, 2400, 240000),
                    ("item.card.spark_wisp", EquipmentSlot.Weapon, 999, 1, 0, 200, 800, 80, 8000),
                    ("item.card.training_slime", EquipmentSlot.Armor, 999, 1, 0, 200, 800, 80, 8000)
                }),
            "stacking to 999, selling to no shop, every one listable at ten times its base at most");
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Type == ItemType.Card)
                .Select(item => (item.Definition.Id.Value, item.Prefix, Bonus(item.Definition.Card!.Bonus)))
                .OrderBy(card => card.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.card.forest_crawler", "Keen", "attack 8"),
                    ("item.card.gloom_wisp", "Bane", "boss 20"),
                    ("item.card.grotto_crawler", "Shelled", "defense 20 maxHp 30"),
                    ("item.card.slime_monarch", "Royal", "maxHp 40 reduction 25"),
                    ("item.card.spark_wisp", "Arcane", "int 1 magicAttack 10"),
                    ("item.card.training_slime", "Hardy", "vit 1 maxHp 20")
                }));
        Assert.That(
            result.Content.Monsters
                .Select(monster => monster.Definition)
                .Select(monster => (Id: monster.Id.Value, Item: monster.Drops.Last().Item.Value,
                    monster.Drops.Last().Chance, monster.Drops.Last().MinAmount, monster.Drops.Last().MaxAmount))
                .OrderBy(monster => monster.Id, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("monster.forest_crawler", "item.card.forest_crawler", 0.001, 1, 1),
                    ("monster.gloom_wisp", "item.card.gloom_wisp", 0.001, 1, 1),
                    ("monster.grotto_crawler", "item.card.grotto_crawler", 0.001, 1, 1),
                    ("monster.slime_monarch", "item.card.slime_monarch", 0.01, 1, 1),
                    ("monster.spark_wisp", "item.card.spark_wisp", 0.001, 1, 1),
                    ("monster.training_slime", "item.card.training_slime", 0.001, 1, 1)
                }),
            "each monster's own card last, so every earlier draw keeps its place");
        Assert.That(
            result.Content.Items
                .Where(item => item.Definition.Slot != EquipmentSlot.None)
                .Select(item => (item.Definition.Id.Value, item.Definition.Slots))
                .OrderBy(item => item.Value, StringComparer.Ordinal),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.cloth", 1), ("item.armor.leather", 1), ("item.armor.monarch_mantle", 1),
                    ("item.weapon.ash_staff", 2), ("item.weapon.iron_sword", 2), ("item.weapon.training_staff", 1),
                    ("item.weapon.training_sword", 1)
                }));
    }
}
}
