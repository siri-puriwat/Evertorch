using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     Ailments as content (Content Pipeline §4, §5, §7; Gameplay Systems §9.1, §11.2, §11.7): a status's server-only
///     <c>ailment</c> and <c>durationMs</c>, a damage effect's rider, an ailment skill's chance, a heal's and a
///     consumable's cures, and a card's <c>inflict</c> and <c>resist</c>, their bounds and cross-checks, and the server
///     package that carries them all while the client package carries none.
/// </summary>
[TestFixture]
public sealed class AilmentContentTests
{
    private const string Poisoned = "status-effects/poisoned.yml";
    private const string FocusStatus = "status-effects/focus.yml";
    private const string WindStatus = "status-effects/wind_weapon.yml";
    private const string Bite = "skills/slime_bite.yml";
    private const string Venom = "skills/venom_spit.yml";
    private const string FocusSkill = "skills/focus.yml";
    private const string Converter = "items/wind_converter.yml";
    private const string Job = "jobs/adventurer.yml";
    private const string Card = "items/venom_card.yml";
    private const string Rider = "chance: 0.2087, durationMs: 23167";

    private const string MendText =
        "id: skill.mend\n"
        + "displayName: Mend\n"
        + "targetType: ally\n"
        + "server:\n"
        + "  range: 6.0\n"
        + "  levels:\n"
        + "    - effect:\n"
        + "        heal: { hp: 40, cures: [poison, silence, blind] }\n"
        + "client:\n"
        + "  icon: skill_mend\n";

    private const string WeaponCardText =
        "id: item.card.venom\n"
        + "displayName: Venom Card\n"
        + "type: card\n"
        + "stackLimit: 999\n"
        + "fits: weapon\n"
        + "server:\n"
        + "  weight: 1\n"
        + "  sellPrice: 0\n"
        + "  card:\n"
        + "    attack: 8\n"
        + "    inflict: { poison: 0.0731 }\n"
        + "    removalFee: 200\n"
        + "client:\n"
        + "  icon: item_slime_gel\n"
        + "  model: pickup_slime_gel\n"
        + "  prefix: Venom\n"
        + "  description: Attack +8 and a 7.31% chance to poison on a hit.\n";

    private const string ArmorCardText =
        "id: item.card.warded\n"
        + "displayName: Warded Card\n"
        + "type: card\n"
        + "stackLimit: 999\n"
        + "fits: armor\n"
        + "server:\n"
        + "  weight: 1\n"
        + "  sellPrice: 0\n"
        + "  card:\n"
        + "    resist: { stun: 30, freeze: 50 }\n"
        + "    removalFee: 600\n"
        + "client:\n"
        + "  icon: item_slime_gel\n"
        + "  model: pickup_slime_gel\n"
        + "  prefix: Warded\n"
        + "  description: Stun resistance +30% and freeze resistance +50%.\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentPipelineResult Built(ContentWorkspace workspace)
    {
        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);
        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        return result;
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

    private static JsonElement FirstEffect(ContentPackage package, string id)
    {
        return Definition(package, "skills.json", id).GetProperty("levels")[0].GetProperty("effect");
    }

    private static void AssertReports(ContentPipelineResult result, string file, string field, string message)
    {
        Assert.That(result.Packages, Is.Null);
        Assert.That(
            result.Diagnostics.Count(diagnostic =>
                diagnostic.File == file && diagnostic.FieldPath == field && diagnostic.Message.Contains(message)),
            Is.EqualTo(1),
            Describe(result));
    }

    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "inflict: { poison: 0 }", "server.card.inflict.poison",
        "must be a chance from 0.0001 to 1")]
    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "inflict: { poison: 1.5 }", "server.card.inflict.poison",
        "must be a chance from 0.0001 to 1")]
    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "inflict: { poison: 0.07315 }",
        "server.card.inflict.poison", "with at most four decimals")]
    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "inflict: { none: 0.0731 }", "server.card.inflict.none",
        "must be one of: poison, stun, freeze, sleep, silence, blind")]
    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "inflict: { confusion: 0.0731 }",
        "server.card.inflict.confusion", "must be one of: poison, stun")]
    [TestCase(WeaponCardText, "poison", "stun", "server.card.inflict.stun", "names ailment 'stun', which no status is")]
    [TestCase(WeaponCardText, "inflict: { poison: 0.0731 }", "resist: { poison: 30 }", "server.card.resist",
        "is not for a card that fits a weapon")]
    [TestCase(WeaponCardText, "7.31% chance", "7.3% chance", "client.description",
        "must state every value of the card above 0, and leaves out 7.31")]
    [TestCase(WeaponCardText, "7.31% chance", "123456789012345678901234567890% chance", "client.description",
        "leaves out 7.31")]
    [TestCase(WeaponCardText, "to poison on", "to sicken on", "client.description",
        "must name every ailment of the card, and leaves out poison")]
    [TestCase(ArmorCardText, "resist: { stun: 30, freeze: 50 }", "inflict: { poison: 0.05 }", "server.card.inflict",
        "is only for a card that fits a weapon")]
    [TestCase(ArmorCardText, "stun: 30", "stun: 101", "server.card.resist.stun", "must be between 1 and 100")]
    [TestCase(ArmorCardText, "stun: 30", "stun: 0", "server.card.resist.stun", "must be between 1 and 100")]
    [TestCase(ArmorCardText, "freeze resistance +50%", "freeze resistance", "client.description",
        "leaves out 50")]
    [TestCase(ArmorCardText, "Stun resistance", "Daze resistance", "client.description",
        "must name every ailment of the card, and leaves out stun")]
    public void Run_WhenACardsAilmentIsBroken_ReportsTheField(
        string text,
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(Card, text.Replace(oldText, newText, StringComparison.Ordinal));

        AssertReports(ContentPipeline.Run(workspace.ContentRoot), Card, expectedFieldPath, expectedMessagePart);
    }

    [TestCase(Poisoned, "  ailment: poison\n", "  ailment: confusion\n", "server.ailment",
        "must be one of: poison, stun, freeze, sleep, silence, blind")]
    [TestCase(Poisoned, "  ailment: poison\n", "  ailment: none\n", "server.ailment", "must be one of: poison")]
    [TestCase(Poisoned, "  durationMs: 13127\n", "", "server.durationMs", "required field is missing")]
    [TestCase(Poisoned, "  durationMs: 13127\n", "  durationMs: 0\n", "server.durationMs",
        "must be between 1 and 86400000")]
    [TestCase(Poisoned, "  durationMs: 13127\n", "  durationMs: 86400001\n", "server.durationMs",
        "must be between 1 and 86400000")]
    [TestCase(FocusStatus, "displayName: Focus\n", "displayName: Focus\nserver:\n  durationMs: 1000\n",
        "server.durationMs", "is only for an ailment's status")]
    [TestCase(WindStatus, "  attackElement: wind\n", "  attackElement: wind\n  ailment: stun\n  durationMs: 4000\n",
        "server.ailment", "is not for a status that sets an attack element")]
    [TestCase(Bite, "damage: { ratio: 150, ailment:", "heal: { hp: 150, ailment:",
        "server.levels[0].effect.heal.ailment", "unknown field")]
    [TestCase(Bite, "status: status.poisoned, " + Rider, "status: status.focus, " + Rider,
        "server.levels[0].effect.damage.ailment.status", "names 'status.focus', which is no ailment's status")]
    [TestCase(Bite, "status: status.poisoned, " + Rider, "status: status.missing, " + Rider,
        "server.levels[0].effect.damage.ailment.status", "references unknown status effect 'status.missing'")]
    [TestCase(Bite, Rider, "chance: 0, durationMs: 23167", "server.levels[0].effect.damage.ailment.chance",
        "must be a chance from 0.0001 to 1, with at most four decimals")]
    [TestCase(Bite, Rider, "chance: 1.0001, durationMs: 23167", "server.levels[0].effect.damage.ailment.chance",
        "must be a chance from 0.0001 to 1")]
    [TestCase(Bite, Rider, "chance: 0.20871, durationMs: 23167", "server.levels[0].effect.damage.ailment.chance",
        "with at most four decimals")]
    [TestCase(Bite, Rider, "chance: 2e-1, durationMs: 23167", "server.levels[0].effect.damage.ailment.chance",
        "must be a chance")]
    [TestCase(Bite, Rider, "chance: 0.2087, durationMs: 0", "server.levels[0].effect.damage.ailment.durationMs",
        "must be between 1 and 86400000")]
    [TestCase(Bite, Rider, "chance: 0.2087", "server.levels[0].effect.damage.ailment.durationMs",
        "required field is missing")]
    [TestCase(Bite, "damage: { ratio: 150, ailment:", "damage: { ratio: 150, cures: [poison], ailment:",
        "server.levels[0].effect.damage.cures", "unknown field")]
    [TestCase(FocusSkill, "durationMs: 43117,", "durationMs: 43117, chance: 0.5,",
        "server.levels[0].effect.status.chance", "is only for an ailment's status")]
    [TestCase(Venom, "statPercent: {}", "statPercent: { agi: -10 }", "server.levels[0].effect.status.statPercent",
        "must be all 0 for an ailment's status 'status.poisoned'")]
    [TestCase(Venom, "targetType: enemy\n", "targetType: self\n", "targetType",
        "must be enemy, or self with an area, for an ailment's status 'status.poisoned'")]
    [TestCase(Venom, "chance: 0.6113", "chance: 0.61135", "server.levels[0].effect.status.chance",
        "with at most four decimals")]
    [TestCase(Converter, "cures: [poison]", "cures: [poison, poison]", "server.effect.cures[1]",
        "names poison more than once")]
    [TestCase(Converter, "cures: [poison]", "cures: [confusion]", "server.effect.cures[0]",
        "must be one of: poison, stun")]
    [TestCase(Converter, "cures: [poison]", "cures: []", "server.effect.cures", "must name at least one ailment")]
    [TestCase(Converter, "cures: [poison]", "cures: poison", "server.effect.cures", "must be a sequence")]
    public void Run_WhenAnAilmentFieldIsBroken_ReportsTheField(
        string file,
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(file, oldText, newText);

        AssertReports(ContentPipeline.Run(workspace.ContentRoot), file, expectedFieldPath, expectedMessagePart);
    }

    [Test]
    public void RepositoryContent_GivesHeavyBlowItsStun_AndTheArcanistFrostLance()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        AuthoredSkill blow = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.heavy_blow");
        AuthoredSkill lance = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.frost_lance");

        Assert.That(
            blow.Definition.Levels.Select(level => (level.Effect.Rider!.Status.Value,
                level.Effect.Rider.ChanceBasisPoints,
                level.Effect.Rider.DurationMs)),
            Is.EqualTo(new[] { 500, 1000, 1500, 2000, 2500 }.Select(chance => ("status.stunned", chance, 4000))));
        Assert.That(blow.Description, Does.Contain("stun"));
        Assert.That(
            lance.Definition.Levels.Select(level => (level.SpCost, level.Effect.DamageRatioPercent,
                level.Effect.Rider!.ChanceBasisPoints, level.Effect.Rider.DurationMs)),
            Is.EqualTo(new[]
            {
                (12, 100, 3000, 4000), (14, 110, 3500, 5000), (16, 120, 4000, 6000), (18, 130, 4500, 7000),
                (20, 140, 5000, 8000)
            }));
        Assert.That(
            (lance.Definition.TargetType, lance.Definition.DamageType, lance.Definition.Element, lance.Definition.Range,
                lance.Definition.Requires!.Skill.Value, lance.Definition.Requires.Level),
            Is.EqualTo((SkillTargetType.Enemy, (SkillDamageType?)SkillDamageType.Magical,
                (SkillElement?)SkillElement.Water, 8d, "skill.arcane_bolt", 2)));
        Assert.That(
            lance.Definition.Levels.Select(level => (level.FixedCastMs, level.VariableCastMs, level.AfterCastDelayMs)),
            Is.All.EqualTo((300, 800, 800)));
        Assert.That(
            (lance.Icon, lance.Projectile, lance.Description!.Contains("freeze"), lance.Description.Contains("Water")),
            Is.EqualTo(("skill_frost_lance", "projectile_spark", true, true)));
    }

    [Test]
    public void RepositoryContent_GivesTheMonstersTheirAilmentSkills_AndSparkBoltAndGraveBiteTheirRiders()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty, Describe(result));

        SkillDefinition Skill(string id)
        {
            return result.Content.Skills.Single(skill => skill.Definition.Id.Value == id).Definition;
        }

        string Rider(string id)
        {
            AilmentRider rider = Skill(id).Levels.Single().Effect.Rider!;
            return $"{rider.Status.Value} {rider.ChanceBasisPoints} {rider.DurationMs}";
        }

        string Pure(string id)
        {
            SkillDefinition skill = Skill(id);
            SkillLevel level = skill.Levels.Single();
            return $"{skill.TargetType} {skill.AreaRadius} {skill.Range} {level.FixedCastMs} {level.CooldownMs} "
                + $"{level.Effect.Status.Value} {level.Effect.StatusDurationMs} {level.Effect.ChanceBasisPoints}";
        }

        string Tried(string monster)
        {
            return string.Join(" ", result.Content.Monsters.Single(entry => entry.Definition.Id.Value == monster)
                .Definition.Skills.Select(entry => $"{entry.Skill.Value} {entry.Chance}"));
        }

        Assert.That(
            (Rider("skill.venom_bite"), Rider("skill.crushing_bite"), Rider("skill.spark_bolt"),
                Rider("skill.grave_bite")),
            Is.EqualTo(("status.poisoned 2000 20000", "status.stunned 1500 4000", "status.blinded 1500 20000",
                "status.poisoned 2500 20000")));
        Assert.That(
            (Skill("skill.venom_bite").Element, Skill("skill.venom_bite").Levels.Single().Effect.DamageRatioPercent,
                Skill("skill.crushing_bite").Element,
                Skill("skill.crushing_bite").Levels.Single().Effect.DamageRatioPercent),
            Is.EqualTo(((SkillElement?)SkillElement.Poison, 100, (SkillElement?)SkillElement.Earth, 120)));
        Assert.That(
            (Pure("skill.hush"), Pure("skill.lull"), Pure("skill.frost_breath")),
            Is.EqualTo(("Enemy 0 6 800 12000 status.silenced 15000 10000",
                "Enemy 0 6 1000 15000 status.asleep 15000 10000", "Self 4 0 1500 20000 status.frozen 8000 10000")));
        Assert.That(
            new[]
            {
                Tried("monster.forest_crawler"), Tried("monster.grotto_crawler"), Tried("monster.gloom_wisp"),
                Tried("monster.hollow_wisp"), Tried("monster.slime_monarch")
            },
            Is.EqualTo(new[]
            {
                "skill.venom_bite 0.15", "skill.crushing_bite 0.1", "skill.numbing_spark 0.15 skill.hush 0.15",
                "skill.lull 0.15", "skill.quake_slam 0.25 skill.frost_breath 0.15"
            }));
    }

    [Test]
    public void RepositoryContent_HoldsTheSixAilmentsStatuses()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(result.Diagnostics, Is.Empty, Describe(result));

        Assert.That(
            result.Content.StatusEffects
                .Select(status => status.Definition)
                .Where(status => status.Ailment != Ailment.None)
                .Select(status => (status.Id.Value, status.DisplayName, status.Ailment, status.DurationMs))
                .OrderBy(status => status.Ailment),
            Is.EqualTo(
                new[]
                {
                    ("status.poisoned", "Poisoned", Ailment.Poison, 20_000),
                    ("status.stunned", "Stunned", Ailment.Stun, 4_000),
                    ("status.frozen", "Frozen", Ailment.Freeze, 8_000),
                    ("status.asleep", "Asleep", Ailment.Sleep, 15_000),
                    ("status.silenced", "Silenced", Ailment.Silence, 15_000),
                    ("status.blinded", "Blinded", Ailment.Blind, 20_000)
                }));
        Assert.That(
            result.Content.StatusEffects
                .Where(status => status.Definition.Ailment != Ailment.None)
                .Select(status => status.Icon),
            Is.EquivalentTo(new[]
            {
                "status_poisoned", "status_stunned", "status_frozen", "status_asleep", "status_silenced",
                "status_blinded"
            }));
    }

    [Test]
    public void Run_ForAHealsCures_ReadsThem_AndAConsumableMayOnlyCure()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write("skills/mend.yml", MendText);
        workspace.Replace(Converter, "    status: { status: status.wind_weapon, durationMs: 61987 }\n", "");

        ContentPipelineResult result = Built(workspace);

        SkillEffect mend = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.mend")
            .Definition.Levels.Single().Effect;
        ItemEffect remedy = result.Content.Items
            .Single(item => item.Definition.Id.Value == "item.consumable.wind_converter").Definition.Effect!;
        Assert.That(mend.Cures, Is.EqualTo(new[] { Ailment.Poison, Ailment.Silence, Ailment.Blind }));
        Assert.That((remedy.Health, remedy.Status), Is.EqualTo((0, (StatusDefinitionId?)null)));
        Assert.That(remedy.Cures, Is.EqualTo(new[] { Ailment.Poison }));
        Assert.That(
            FirstEffect(result.Packages!.Server, "skill.mend").GetProperty("cures").EnumerateArray()
                .Select(name => name.GetString()),
            Is.EqualTo(new[] { "poison", "silence", "blind" }));
        Assert.That(
            Definition(result.Packages.Server, "items.json", "item.consumable.minor_health").GetProperty("effect")
                .GetProperty("cures").GetArrayLength(),
            Is.Zero,
            "every consumable writes its cures, none here");
    }

    [Test]
    public void Run_ForAJobListingAnAilmentSkillOrADamageSkillWithARider_RefusesOnlyTheAilmentSkill()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Job, "skills: [skill.strike]", "skills: [skill.strike, skill.slime_bite, skill.venom_spit]");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(
            result.Diagnostics.Select(diagnostic => (diagnostic.File, diagnostic.FieldPath)),
            Is.EqualTo(new[] { (Job, "server.skills[2]") }),
            Describe(result));
        Assert.That(result.Diagnostics.Single().Message, Does.Contain("which puts a status on an enemy"));
    }

    [Test]
    public void Run_ForAnAreaAilmentSkillCastOnItsCaster_IsValid_AndAJobMayNotListIt()
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace(Venom, "targetType: enemy\n", "targetType: self\narea: { radius: 4.0 }\n");
        workspace.Replace(Venom, "  range: 6.0\n", "  range: 0\n");

        ContentPipelineResult result = Built(workspace);
        workspace.Replace(Job, "skills: [skill.strike]", "skills: [skill.strike, skill.venom_spit]");
        ContentPipelineResult listed = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(
            Definition(result.Packages!.Server, "skills.json", "skill.venom_spit").GetProperty("areaRadius")
                .GetDouble(),
            Is.EqualTo(4d));
        AssertReports(listed, Job, "server.skills[1]", "strikes an area; only a monster casts one");
    }

    [Test]
    public void Run_ForCards_ReadsTheirAilments_AndWritesBothMapsForTheServerAlone()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(Card, WeaponCardText);
        workspace.Write("items/warded_card.yml", ArmorCardText.Replace("stun: 30, ", "", StringComparison.Ordinal)
            .Replace("Stun resistance +30% and freeze", "Freeze", StringComparison.Ordinal));
        workspace.Write(
            "status-effects/frozen.yml",
            "id: status.frozen\ndisplayName: Frozen\nserver:\n  ailment: freeze\n  durationMs: 8000\n");

        ContentPipelineResult result = Built(workspace);

        CardBonus venom = result.Content.Items.Single(item => item.Definition.Id.Value == "item.card.venom")
            .Definition.Card!.Bonus;
        CardBonus warded = result.Content.Items.Single(item => item.Definition.Id.Value == "item.card.warded")
            .Definition.Card!.Bonus;
        Assert.That(
            (venom.ChanceToInflict(Ailment.Poison), venom.ChanceToInflict(Ailment.Stun), venom.Resist.Count),
            Is.EqualTo((731, 0, 0)));
        Assert.That(
            (warded.ResistanceTo(Ailment.Freeze), warded.ResistanceTo(Ailment.Poison), warded.Inflict.Count),
            Is.EqualTo((50, 0, 0)));
        JsonElement card = Definition(result.Packages!.Server, "items.json", "item.card.venom").GetProperty("card");
        Assert.That(
            (card.GetProperty("inflict").GetProperty("poison").GetRawText(),
                card.GetProperty("inflict").EnumerateObject().Count(),
                card.GetProperty("resist").EnumerateObject().Count()),
            Is.EqualTo(("0.0731", 1, 0)));
        Assert.That(
            Definition(result.Packages.Server, "items.json", "item.card.warded").GetProperty("card")
                .GetProperty("resist").GetProperty("freeze").GetInt32(),
            Is.EqualTo(50));
        JsonElement client = Definition(result.Packages.Client, "items.json", "item.card.venom");
        Assert.That(
            client.EnumerateObject().Select(property => property.Name),
            Has.None.EqualTo("inflict").And.None.EqualTo("card"));
    }

    [Test]
    public void Run_ForTheValidFixture_ReadsEachAilmentField_AndWritesThemForTheServerAlone()
    {
        using var workspace = new ContentWorkspace();

        ContentPipelineResult result = Built(workspace);

        StatusEffectDefinition poisoned = result.Content.StatusEffects
            .Single(status => status.Definition.Id.Value == "status.poisoned").Definition;
        SkillEffect bite = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.slime_bite")
            .Definition.Levels.Single().Effect;
        SkillEffect venom = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.venom_spit")
            .Definition.Levels.Single().Effect;
        SkillEffect focus = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.focus")
            .Definition.Levels.Single().Effect;
        Assert.That((poisoned.Ailment, poisoned.DurationMs, poisoned.AttackElement),
            Is.EqualTo((Ailment.Poison, 13127, (Element?)null)));
        Assert.That(
            (bite.Rider!.Status.Value, bite.Rider.ChanceBasisPoints, bite.Rider.DurationMs),
            Is.EqualTo(("status.poisoned", 2087, 23167)));
        Assert.That((venom.ChanceBasisPoints, venom.StatPercent), Is.EqualTo((6113, default(StatPercentages))));
        Assert.That((focus.ChanceBasisPoints, focus.Rider), Is.EqualTo((10_000, (AilmentRider?)null)));

        ContentPackage server = result.Packages!.Server;
        JsonElement status = Definition(server, "status-effects.json", "status.poisoned");
        Assert.That(
            (status.GetProperty("ailment").GetString(), status.GetProperty("durationMs").GetInt32()),
            Is.EqualTo(("poison", 13127)));
        JsonElement rider = FirstEffect(server, "skill.slime_bite").GetProperty("ailment");
        Assert.That(
            (rider.GetProperty("status").GetString(), rider.GetProperty("chance").GetRawText(),
                rider.GetProperty("durationMs").GetInt32()),
            Is.EqualTo(("status.poisoned", "0.2087", 23167)));
        Assert.That(FirstEffect(server, "skill.venom_spit").GetProperty("chance").GetRawText(), Is.EqualTo("0.6113"));
        Assert.That(
            FirstEffect(server, "skill.focus").TryGetProperty("chance", out _),
            Is.False,
            "a status that is no ailment is certain, and says nothing");
        Assert.That(
            Definition(server, "status-effects.json", "status.focus").TryGetProperty("ailment", out _),
            Is.False);
        Assert.That(
            Definition(result.Packages.Client, "status-effects.json", "status.poisoned").EnumerateObject()
                .Select(property => property.Name),
            Is.EqualTo(new[] { "id", "displayName", "icon" }));
    }

    [Test]
    public void Run_WhenTwoStatusesNameOneAilment_ReportsTheSecond()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(
            "status-effects/venom.yml",
            "id: status.venom\ndisplayName: Venom\nserver:\n  ailment: poison\n  durationMs: 1000\n");

        AssertReports(
            ContentPipeline.Run(workspace.ContentRoot),
            "status-effects/venom.yml",
            "server.ailment",
            "names ailment 'poison', which status 'status.poisoned' already is");
    }
}
}
