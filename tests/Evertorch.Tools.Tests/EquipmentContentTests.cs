using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Weapons and armor (Content Pipeline §4; Gameplay Systems §11.1): a stack limit of 1, a weapon's attack,
///     attack-speed penalty, and type, an armor's defense, and optional statistic bonuses, all for the server alone.
/// </summary>
[TestFixture]
public sealed class EquipmentContentTests
{
    private const string Sword = "items/training_sword.yml";
    private const string Armor = "items/cloth_armor.yml";

    private const string SwordText =
        "id: item.weapon.training_sword\n"
        + "displayName: Training Sword\n"
        + "type: weapon\n"
        + "stackLimit: 1\n"
        + "server:\n"
        + "  weight: 50\n"
        + "  sellPrice: 25\n"
        + "  equipment:\n"
        + "    attack: 20\n"
        + "    attackSpeedPenalty: 50\n"
        + "    weaponType: sword\n"
        + "    bonus: { int: 3 }\n"
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

    [TestCase(Sword, "stackLimit: 1", "stackLimit: 2", "stackLimit", "must be 1 for a weapon or armor")]
    [TestCase(Sword, "    attack: 20\n", "", "server.equipment.attack", "required field is missing")]
    [TestCase(Sword, "    attack: 20", "    attack: 20\n    defense: 3", "server.equipment.defense", "unknown field")]
    [TestCase(Sword, "    attack: 20", "    attack: -1", "server.equipment.attack", "between 0 and")]
    [TestCase(Sword, "{ int: 3 }", "{ wis: 3 }", "server.equipment.bonus.wis", "unknown field")]
    [TestCase(Sword, "    weaponType: sword\n", "", "server.equipment.weaponType", "required field is missing")]
    [TestCase(Sword, "weaponType: sword", "weaponType: axe", "server.equipment.weaponType", "sword, staff")]
    [TestCase(
        Sword,
        "attackSpeedPenalty: 50",
        "attackSpeedPenalty: 201",
        "server.equipment.attackSpeedPenalty",
        "between 0 and 200")]
    [TestCase(
        Armor,
        "    defense: 8\n",
        "    defense: 8\n    weaponType: staff\n",
        "server.equipment.weaponType",
        "unknown field")]
    [TestCase(Armor, "    defense: 8\n", "    attack: 8\n", "server.equipment.defense", "required field is missing")]
    [TestCase(
        Armor,
        "  model: pickup_cloth_armor\n",
        "  model: pickup_cloth_armor\n  held: weapon_training_sword\n",
        "client.held",
        "is only for a weapon")]
    [TestCase(
        Armor,
        "  equipment:\n    defense: 8\n",
        "",
        "server.equipment",
        "required field is missing")]
    public void Run_WhenAWeaponOrArmorIsBroken_ReportsThatField(
        string file,
        string oldText,
        string newText,
        string field,
        string message)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write(Sword, SwordText);
            workspace.Write(Armor, ArmorText);
            workspace.Replace(file, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.File == file && diagnostic.FieldPath == field && diagnostic.Message.Contains(message)),
                Is.True,
                Describe(result));
        }
    }

    [Test]
    public void Build_WithAWeaponAndArmor_WritesTheirValuesForTheServerOnly()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write(Sword, SwordText);
            workspace.Write(Armor, ArmorText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            JsonElement sword = Item(result.Packages!.Server, "item.weapon.training_sword").GetProperty("equipment");
            JsonElement armor = Item(result.Packages.Server, "item.armor.cloth").GetProperty("equipment");
            Assert.That(
                (sword.GetProperty("attack").GetInt32(), sword.GetProperty("attackSpeedPenalty").GetInt32(),
                    sword.GetProperty("defense").GetInt32(), sword.GetProperty("bonus").GetProperty("int").GetInt32()),
                Is.EqualTo((20, 50, 0, 3)));
            Assert.That(sword.GetProperty("weaponType").GetString(), Is.EqualTo("sword"));
            Assert.That(armor.TryGetProperty("weaponType", out _), Is.False, "an armor has no weapon type");
            Assert.That(
                (armor.GetProperty("defense").GetInt32(), armor.GetProperty("attack").GetInt32()),
                Is.EqualTo((8, 0)));
            JsonElement client = Item(result.Packages.Client, "item.weapon.training_sword");
            Assert.That(client.TryGetProperty("equipment", out _), Is.False);
            Assert.That(client.GetProperty("type").GetString(), Is.EqualTo("weapon"));
        }
    }

    [Test]
    public void Build_WithAWeaponsHeldModel_WritesItForTheClientAlone()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write(Sword, SwordText + "  held: weapon_training_sword\n");
            workspace.Write(Armor, ArmorText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            JsonElement sword = Item(result.Packages!.Client, "item.weapon.training_sword");
            Assert.That(sword.GetProperty("held").GetString(), Is.EqualTo("weapon_training_sword"));
            Assert.That(Item(result.Packages.Client, "item.armor.cloth").TryGetProperty("held", out _), Is.False);
            Assert.That(
                Item(result.Packages.Server, "item.weapon.training_sword").TryGetProperty("held", out _),
                Is.False,
                "a presentation key stays out of the server's package");
        }
    }

    [Test]
    public void Run_WhenAMaterialHasEquipment_ReportsIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace("items/slime_gel.yml", "  sellPrice: 73219\n",
                "  sellPrice: 73219\n  equipment:\n    defense: 1\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => (diagnostic.FieldPath, diagnostic.Message)),
                Has.Some.EqualTo(("server.equipment", "is only for a weapon or armor")),
                Describe(result));
        }
    }
}
}
