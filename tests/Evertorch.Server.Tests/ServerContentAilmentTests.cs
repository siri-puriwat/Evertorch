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
///     The loader reads a status's ailment and its duration, a damage effect's rider, an ailment skill's chance, a
///     heal's and a consumable's cures, and a card's <c>inflict</c> and <c>resist</c>, and refuses what the tools refuse
///     of them (Content Pipeline §7; Gameplay Systems §9.1, §11.2, §11.7). The repository's package carries the six
///     ailments' statuses, every card's two maps, and every heal's and consumable's cures; an edit re-signs it.
/// </summary>
[TestFixture]
public sealed class ServerContentAilmentTests
{
    private const string Skills = "skills.json";
    private const string Statuses = "status-effects.json";
    private const string Items = "items.json";
    private const string Potion = "item.consumable.minor_health";
    private const string Keen = "item.card.forest_crawler";
    private const string Hardy = "item.card.training_slime";
    private const string Stun = "{\"status\": \"status.stunned\", \"chance\": 0.05, \"durationMs\": 4000}";
    private const string NoChange = "{\"str\": 0, \"agi\": 0, \"vit\": 0, \"int\": 0, \"dex\": 0, \"luk\": 0}";

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

    private static JsonNode Find(JsonArray definitions, string id)
    {
        return definitions.Single(definition => (string?)definition!["id"] == id)!;
    }

    private static SkillEffect FirstEffect(ServerContent content, string skill)
    {
        return content.Skills[new SkillDefinitionId(skill)].Levels[0].Effect;
    }

    [TestCase(Statuses, "status.poisoned", "ailment", "\"confusion\"", "unknown ailment 'confusion'")]
    [TestCase(Statuses, "status.poisoned", "ailment", "\"none\"", "unknown ailment 'none'")]
    [TestCase(Statuses, "status.poisoned", "durationMs", "0", "must be at least 1")]
    [TestCase(Statuses, "status.poisoned", "durationMs", "86400001", "must be at most 86400000")]
    [TestCase(Statuses, "status.focus", "durationMs", "1000", "unknown property")]
    [TestCase(Statuses, "status.fire_weapon", "ailment", "\"stun\"", "is not for a status that sets an attack element")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.stunned\", \"chance\": 0, \"durationMs\": 4000}", "must be a chance from 0.0001 to 1")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.stunned\", \"chance\": 0.00005, \"durationMs\": 4000}", "with at most four decimals")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.stunned\", \"chance\": 1.5, \"durationMs\": 4000}", "must be a chance from 0.0001 to 1")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.stunned\", \"chance\": 0.05}", "durationMs: required property is missing")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.stunned\", \"chance\": 0.05, \"durationMs\": 4000, \"ratio\": 1}",
        "ratio: unknown property")]
    [TestCase(Skills, "skill.mend", "levels[0].effect.cures", "[\"confusion\"]", "unknown ailment 'confusion'")]
    [TestCase(Skills, "skill.mend", "levels[0].effect.cures", "[\"poison\", \"poison\"]", "appears more than once")]
    [TestCase(Skills, "skill.mend", "levels[0].effect.cures", "\"poison\"", "must be an array")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.cures", "[]", "unknown property")]
    [TestCase(Items, Potion, "effect.cures", "[\"sleep\", \"sleep\"]", "appears more than once")]
    [TestCase(Items, Keen, "card.inflict", "{\"poison\": 1.5}", "must be a chance from 0.0001 to 1")]
    [TestCase(Items, Keen, "card.inflict", "{\"none\": 0.05}", "unknown name")]
    [TestCase(Items, Keen, "card.inflict", "[]", "must be an object")]
    [TestCase(Items, Keen, "card.resist", "{\"stun\": 30}", "is not for a card that fits a weapon")]
    [TestCase(Items, Hardy, "card.inflict", "{\"poison\": 0.05}", "is only for a card that fits a weapon")]
    [TestCase(Items, Hardy, "card.resist", "{\"stun\": 101}", "must be at most 100")]
    [TestCase(Items, Hardy, "card.resist", "{\"stun\": 0}", "must be at least 1")]
    public void Load_ForAnAilmentFieldOutOfBounds_RefusesIt(
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

    [TestCase(Statuses, "status.stunned", "ailment", "\"poison\"",
        "status-effects.json: status.stunned: is the ailment of 'status.poisoned' too")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.focus\", \"chance\": 0.05, \"durationMs\": 4000}",
        "skills.json: skill.heavy_blow: brings 'status.focus', which is no ailment's status")]
    [TestCase(Skills, "skill.heavy_blow", "levels[0].effect.ailment",
        "{\"status\": \"status.missing\", \"chance\": 0.05, \"durationMs\": 4000}",
        "skills.json: skill.heavy_blow: brings unknown status effect 'status.missing'")]
    [TestCase(Skills, "skill.focus", "levels[0].effect.chance", "0.5",
        "skills.json: skill.focus: has a chance, which is only for an ailment's status")]
    [TestCase(Skills, "skill.numbing_spark", "levels[0].effect.status", "\"status.silenced\"",
        "skills.json: skill.numbing_spark: changes statistics with ailment status 'status.silenced'")]
    [TestCase(Skills, "skill.focus", "levels[0].effect.status", "\"status.asleep\"",
        "skills.json: skill.focus: puts ailment status 'status.asleep' on neither an enemy nor the players around its "
        + "caster")]
    [TestCase(Skills, "skill.focus", "areaRadius", "4",
        "skills.json: skill.focus: strikes an area with 'status.focus', no ailment")]
    public void Load_ForAnAilmentThatDoesNotFit_RefusesIt(string file, string id, string path, string json,
        string problem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, file, id, path, json);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Member(problem), string.Join("\n", problems));
    }

    [Test]
    public void Load_ForAnAreaAilmentSkillCastOnItsCaster_ReadsIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Skills, "skill.quake_slam", "levels[0].effect",
            $"{{\"status\": \"status.frozen\", \"durationMs\": 8000, \"statPercent\": {NoChange}, \"chance\": 1}}");

        ServerContent content = ServerContentLoader.Load(files);

        SkillDefinition breath = content.Skills[new SkillDefinitionId("skill.quake_slam")];
        Assert.That(
            (breath.TargetType, breath.HasArea, breath.Levels[0].Effect.Status.Value,
                breath.Levels[0].Effect.ChanceBasisPoints),
            Is.EqualTo((SkillTargetType.Self, true, "status.frozen", 10_000)));
    }

    [Test]
    public void Load_ForEditedAilmentFields_ReadsEachOne()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, Skills, "skill.heavy_blow", "levels[0].effect.ailment", Stun);
        PackageFixture.SetValue(files, Skills, "skill.numbing_spark", "levels[0].effect.status", "\"status.silenced\"");
        PackageFixture.SetValue(files, Skills, "skill.numbing_spark", "levels[0].effect.statPercent", NoChange);
        PackageFixture.SetValue(files, Skills, "skill.numbing_spark", "levels[0].effect.chance", "0.6");
        PackageFixture.SetValue(files, Skills, "skill.mend", "levels[0].effect.cures",
            "[\"poison\", \"silence\", \"blind\"]");
        PackageFixture.SetValue(files, Items, Potion, "effect.cures", "[\"blind\"]");
        PackageFixture.SetValue(files, Items, Keen, "card.inflict", "{\"poison\": 0.05, \"silence\": 1}");
        PackageFixture.SetValue(files, Items, Hardy, "card.resist", "{\"stun\": 30, \"freeze\": 100}");

        ServerContent content = ServerContentLoader.Load(files);

        AilmentRider rider = FirstEffect(content, "skill.heavy_blow").Rider!;
        SkillEffect hush = FirstEffect(content, "skill.numbing_spark");
        CardBonus keen = content.Items[new ItemDefinitionId(Keen)].Card!.Bonus;
        CardBonus hardy = content.Items[new ItemDefinitionId(Hardy)].Card!.Bonus;
        Assert.That(
            (rider.Status.Value, rider.ChanceBasisPoints, rider.DurationMs),
            Is.EqualTo(("status.stunned", 500, 4000)));
        Assert.That(
            content.Skills[new SkillDefinitionId("skill.heavy_blow")].Levels.Skip(1)
                .Select(level => level.Effect.Rider!.ChanceBasisPoints),
            Is.EqualTo(new[] { 1000, 1500, 2000, 2500 }),
            "a rider is each level's own");
        Assert.That((hush.Status.Value, hush.ChanceBasisPoints), Is.EqualTo(("status.silenced", 6000)));
        Assert.That(
            FirstEffect(content, "skill.mend").Cures,
            Is.EqualTo(new[] { Ailment.Poison, Ailment.Silence, Ailment.Blind }));
        Assert.That(content.Items[new ItemDefinitionId(Potion)].Effect!.Cures, Is.EqualTo(new[] { Ailment.Blind }));
        Assert.That(
            (keen.ChanceToInflict(Ailment.Poison), keen.ChanceToInflict(Ailment.Silence),
                keen.ChanceToInflict(Ailment.Stun)),
            Is.EqualTo((500, 10_000, 0)));
        Assert.That(
            (hardy.ResistanceTo(Ailment.Stun), hardy.ResistanceTo(Ailment.Freeze), hardy.ResistanceTo(Ailment.Blind)),
            Is.EqualTo((30, 100, 0)));
    }

    [Test]
    public void Load_ForTheRepositoryContent_ReadsTheSixAilmentsStatuses_TheCuresAndTheCardsAilments()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(
            content.StatusEffects.Values
                .Where(status => status.Ailment != Ailment.None)
                .OrderBy(status => status.Ailment)
                .Select(status => (status.Id.Value, status.Ailment, status.DurationMs, status.AttackElement)),
            Is.EqualTo(
                new[]
                {
                    ("status.poisoned", Ailment.Poison, 20_000, null),
                    ("status.stunned", Ailment.Stun, 4_000, null),
                    ("status.frozen", Ailment.Freeze, 8_000, null),
                    ("status.asleep", Ailment.Sleep, 15_000, null),
                    ("status.silenced", Ailment.Silence, 15_000, (Element?)null),
                    ("status.blinded", Ailment.Blind, 20_000, (Element?)null)
                }));
        Assert.That(content.StatusEffects, Has.Count.EqualTo(15));
        Assert.That(
            content.StatusEffects.Values.Where(status => status.Ailment == Ailment.None)
                .Select(status => status.DurationMs),
            Is.All.Zero);
        Assert.That(
            content.Skills.Values.Where(skill => skill.Levels.Any(level => level.Effect.Cures.Count > 0))
                .Select(skill => skill.Id.Value),
            Is.EqualTo(new[] { "skill.mend" }));
        Assert.That(
            content.Skills[new SkillDefinitionId("skill.mend")].Levels.Select(level => level.Effect.Cures),
            Is.All.EqualTo(new[] { Ailment.Poison, Ailment.Silence, Ailment.Blind }), "every level");
        Assert.That(
            content.Items.Values.Where(item => item.Card != null)
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .Select(item => (item.Id.Value, item.Card!.Bonus.Inflict.Count, item.Card.Bonus.Resist.Count)),
            Is.EqualTo(new[]
            {
                ("item.card.barrow_crawler", 0, 1), ("item.card.forest_crawler", 1, 0),
                ("item.card.forest_crawler_stride", 0, 0), ("item.card.gloom_wisp", 1, 0),
                ("item.card.gloom_wisp_veil", 0, 1), ("item.card.grotto_crawler", 0, 1),
                ("item.card.grotto_crawler_bulwark", 0, 0), ("item.card.hollow_wisp", 1, 0),
                ("item.card.hollow_wisp_poise", 0, 0), ("item.card.slime_monarch", 0, 1),
                ("item.card.spark_wisp", 0, 0), ("item.card.spark_wisp_insight", 0, 0),
                ("item.card.training_slime", 0, 0)
            }),
            "six cards with an ailment, Hardy and Arcane without");
        Assert.That(
            content.Items.Values.Where(item => item.Effect != null && item.Effect.Cures.Count > 0)
                .Select(item => (item.Id.Value, item.Effect!.Health, item.Effect.Spirit, item.Effect.Cures.Count))
                .OrderBy(cure => cure.Value, StringComparer.Ordinal),
            Is.EqualTo(new[]
            {
                ("item.consumable.grand_remedy", 120, 40, 3), ("item.consumable.remedy", 0, 0, 3)
            }));
    }

    [Test]
    public void Load_WithoutACardsInflictOrAHealsCures_RefusesIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        JsonArray items = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        Find(items, Keen)["card"]!.AsObject().Remove("inflict");
        files[Items] = Encoding.UTF8.GetBytes(items.Root.ToJsonString());
        JsonArray skills = JsonNode.Parse(files[Skills])!["definitions"]!.AsArray();
        Find(skills, "skill.mend")["levels"]![0]!["effect"]!.AsObject().Remove("cures");
        files[Skills] = Encoding.UTF8.GetBytes(skills.Root.ToJsonString());
        PackageFixture.RewriteManifest(files);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(
            problems.Where(problem => problem.Contains("required property is missing", StringComparison.Ordinal))
                .Select(problem => problem.Substring(problem.LastIndexOf('.') + 1)),
            Is.EquivalentTo(new[] { "inflict: required property is missing", "cures: required property is missing" }),
            string.Join("\n", problems));
    }
}
}
