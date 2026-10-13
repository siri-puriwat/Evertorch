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
///     The loader reads a monster's element, race, and size, a damage skill's element, a status's attack element, the
///     status a consumable starts, and a card's groups and armor element, and refuses what the tools refuse of them
///     (Content Pipeline §7; Gameplay Systems §9, §9.1, §10, §11.7). The repository's package carries every monster's
///     facts and every damage skill's element; an edit re-signs it.
/// </summary>
[TestFixture]
public sealed class ServerContentElementTests
{
    private const string Monsters = "monsters.json";
    private const string Skills = "skills.json";
    private const string Statuses = "status-effects.json";
    private const string Items = "items.json";
    private const string Slime = "monster.training_slime";
    private const string Potion = "item.consumable.minor_health";
    private const string Keen = "item.card.forest_crawler";
    private const string Hardy = "item.card.training_slime";
    private const string Shelled = "item.card.grotto_crawler";

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

    [TestCase("element", "\"lightning\"", "unknown value 'lightning'")]
    [TestCase("element", "\"Water\"", "unknown value 'Water'")]
    [TestCase("element", "3", "must be a string")]
    [TestCase("race", "\"elemental\"", "unknown value 'elemental'")]
    [TestCase("size", "\"huge\"", "unknown value 'huge'")]
    public void Load_ForAFactOutOfItsList_RefusesIt(string path, string json, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string reported = PackageFixture.SetValue(files, Monsters, Slime, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.StartsWith($"{Monsters}: {reported}").And.Contains(message),
            string.Join("\n", problems));
    }

    [TestCase(Skills, "skill.basic_attack", "element", "\"dark\"", "unknown value 'dark'")]
    [TestCase(Skills, "skill.focus", "element", "\"wind\"", "is only for a skill with a damageType")]
    [TestCase(Statuses, "status.focus", "attackElement", "\"weapon\"", "unknown value 'weapon'")]
    [TestCase(Items, Keen, "card.raceDamage", "{\"insect\": 101}", "must be at most 100")]
    [TestCase(Items, Keen, "card.raceDamage", "{\"insect\": 0}", "must be at least 1")]
    [TestCase(Items, Keen, "card.raceDamage", "[]", "must be an object")]
    [TestCase(Items, Keen, "card.sizeDamage", "{\"huge\": 10}", "unknown name")]
    [TestCase(Items, Keen, "card.elementDamage", "{\"weapon\": 10}", "unknown name")]
    [TestCase(Items, Keen, "card.elementDamageReduction", "{\"wind\": 91}", "must be at most 90")]
    [TestCase(Items, Keen, "card.armorElement", "\"earth\"", "is only for a card that fits armor")]
    [TestCase(Items, Hardy, "card.armorElement", "\"weapon\"", "unknown value 'weapon'")]
    [TestCase(Items, Potion, "effect.durationMs", "1000", "unknown property")]
    public void Load_ForAnElementOrAGroupOutOfItsList_RefusesIt(
        string file,
        string id,
        string path,
        string json,
        string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string reported = PackageFixture.SetValue(files, file, id, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.StartsWith($"{file}: {reported}").And.Contains(message),
            string.Join("\n", problems));
    }

    [TestCase("\"status.focus\"", "1200000", "starts status effect 'status.focus', which sets no attack element")]
    [TestCase("\"status.ember\"", "1200000", "starts unknown status effect 'status.ember'")]
    [TestCase("\"status.focus\"", "0", "effect.durationMs: must be at least 1")]
    [TestCase("\"status.focus\"", "86400001", "effect.durationMs: must be at most 86400000")]
    [TestCase("\"Status.Focus\"", "1200000", "effect.status: 'Status.Focus' is not a valid ID of this kind")]
    public void Load_ForAConsumablesStatusOutOfBounds_RefusesIt(string status, string durationMs, string message)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Items, Potion, "effect.status", status);
        PackageFixture.SetValue(files, Items, Potion, "effect.durationMs", durationMs);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.Contains(message), string.Join("\n", problems));
    }

    [Test]
    public void Load_ForACardsGroupsAndArmorElement_ReadsThem_AndAnElementAloneIsAValue()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Items, Keen, "card.raceDamage", "{\"insect\": 10}");
        PackageFixture.SetValue(files, Items, Keen, "card.elementDamage", "{\"earth\": 25, \"water\": 5}");
        PackageFixture.SetValue(files, Items, Keen, "card.sizeDamage", "{\"small\": 15}");
        PackageFixture.SetValue(files, Items, Hardy, "card.elementDamageReduction", "{\"wind\": 20}");
        PackageFixture.SetValue(files, Items, Shelled, "card.defense", "0");
        PackageFixture.SetValue(files, Items, Shelled, "card.maxHp", "0");
        PackageFixture.SetValue(files, Items, Shelled, "card.armorElement", "\"earth\"");

        ServerContent content = ServerContentLoader.Load(files);

        CardBonus keen = content.Items[new ItemDefinitionId(Keen)].Card!.Bonus;
        CardBonus hardy = content.Items[new ItemDefinitionId(Hardy)].Card!.Bonus;
        CardBonus shelled = content.Items[new ItemDefinitionId(Shelled)].Card!.Bonus;
        Assert.That(
            (keen.DamageTo(Race.Insect), keen.DamageTo(Element.Earth), keen.DamageTo(Element.Water),
                keen.DamageTo(BodySize.Small), keen.DamageTo(BodySize.Large), keen.ArmorElement),
            Is.EqualTo((10, 25, 5, 15, 0, Element.Neutral)));
        Assert.That(
            (hardy.ReductionFrom(Element.Wind), hardy.ReductionFrom(Element.Fire), hardy.ArmorElement),
            Is.EqualTo((20, 0, Element.Neutral)));
        Assert.That(
            (shelled.Defense, shelled.MaxHp, shelled.ArmorElement),
            Is.EqualTo((0, 0, Element.Earth)));
    }

    [Test]
    public void Load_ForAConsumableStartingAStatusThatSetsAnElement_ReadsBoth()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Statuses, "status.focus", "attackElement", "\"fire\"");
        PackageFixture.SetValue(files, Items, Potion, "effect.status", "\"status.focus\"");
        PackageFixture.SetValue(files, Items, Potion, "effect.durationMs", "1200000");
        PackageFixture.SetValue(files, Items, Potion, "effect.hp", "0");

        ServerContent content = ServerContentLoader.Load(files);

        ItemEffect effect = content.Items[new ItemDefinitionId(Potion)].Effect!;
        Assert.That(
            (effect.Health, effect.Spirit, effect.Status?.Value, effect.StatusDurationMs),
            Is.EqualTo((0, 0, "status.focus", 1200000)),
            "a status alone is an effect");
        Assert.That(
            content.StatusEffects[new StatusDefinitionId("status.focus")].AttackElement,
            Is.EqualTo(Element.Fire));
    }

    [Test]
    public void Load_ForADamageSkillWithoutAnElement_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        JsonArray definitions = JsonNode.Parse(files[Skills])!["definitions"]!.AsArray();
        definitions.Single(definition => (string?)definition!["id"] == "skill.strike")!.AsObject().Remove("element");
        files[Skills] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1), string.Join("\n", problems));
        Assert.That(
            problems[0],
            Does.StartWith($"{Skills}: definitions[").And.EndWith("].element: required property is missing"));
    }

    [Test]
    public void Load_ForAGroupNamingOneRaceTwice_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, Items, "\"raceDamage\": {}", "\"raceDamage\": {\"insect\": 10, \"insect\": 20}");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Some.EndsWith(".card.raceDamage.insect: appears more than once"),
            string.Join("\n", problems));
    }

    [Test]
    public void Load_ForTheRepositoryContent_GivesEachDamageSkillItsElement_AndNoOtherSkillOne()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Skills.Values
                .Where(skill => skill.Element != null)
                .OrderBy(skill => skill.Id.Value, StringComparer.Ordinal)
                .Select(skill => $"{skill.Id.Value} {skill.Element}"),
            Is.EqualTo(
                new[]
                {
                    "skill.arcane_bolt Ghost", "skill.basic_attack Weapon", "skill.grave_bite Shadow",
                    "skill.heavy_blow Weapon", "skill.quake_slam Earth", "skill.spark_bolt Wind", "skill.strike Weapon"
                }));
        Assert.That(
            content.Skills.Values.Where(skill => skill.DamageType == null).Select(skill => skill.Element),
            Is.All.Null);
    }

    [Test]
    public void Load_ForTheRepositoryContent_GivesEachMonsterItsFacts()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.Monsters.Values
                .OrderBy(monster => monster.Id.Value, StringComparer.Ordinal)
                .Select(monster => $"{monster.Id.Value} {monster.Element} {monster.Race} {monster.Size}"),
            Is.EqualTo(
                new[]
                {
                    "monster.barrow_crawler Undead Undead Medium", "monster.forest_crawler Earth Insect Small",
                    "monster.gloom_wisp Shadow Demon Small", "monster.grotto_crawler Earth Insect Medium",
                    "monster.hollow_wisp Ghost Demon Small", "monster.slime_monarch Water Plant Large",
                    "monster.spark_wisp Wind Formless Small", "monster.training_slime Water Plant Medium"
                }));
    }

    [Test]
    public void Load_WithoutAFact_RefusesTheMonster()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Monsters, "\"race\": \"plant\",", string.Empty);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("definitions[0].race: required property is"));
    }
}
}
