using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     First jobs as content (Content Pipeline §4, §5, §7; Gameplay Systems §2.1, §9, §11.1): a first job names its base
///     job and takes its inherited values in a second pass, learns from the whole tree and carries the base job's
///     points, every job wields at least one weapon type, only a heal may target an ally, a skill may name its
///     projectile, and an NPC's job changes count in its services.
/// </summary>
[TestFixture]
public sealed class FirstJobContentTests
{
    private const string FirstJob = "jobs/vanguard.yml";
    private const string BaseJob = "jobs/adventurer.yml";
    private const string Npc = "npcs/quartermaster.yml";

    // The valid fixture's Adventurer knows Strike, and its job table of two levels caps it at job level 3.
    private const string FirstJobText =
        "id: job.vanguard\n"
        + "displayName: Vanguard\n"
        + "server:\n"
        + "  baseJob: job.adventurer\n"
        + "  health: { base: 60, perLevel: 14 }\n"
        + "  spirit: { base: 20, perLevel: 3 }\n"
        + "  jobExperienceTable: experience.adventurer_job\n"
        + "  skills: [skill.focus]\n"
        + "  weapons: [sword]\n"
        + "client:\n"
        + "  prefab: character_vanguard\n";

    private const string HealText =
        "id: skill.mend\n"
        + "displayName: Mend\n"
        + "targetType: ally\n"
        + "server:\n"
        + "  range: 6.0\n"
        + "  levels:\n"
        + "    - spCost: 12\n"
        + "      castTimeMs: { fixed: 1000 }\n"
        + "      effect:\n"
        + "        heal: { hp: 40 }\n"
        + "client:\n"
        + "  icon: skill_mend\n";

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    private static ContentPipelineResult WithFirstJob(string oldText = "", string newText = "")
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(FirstJob, FirstJobText);
        if (oldText.Length > 0)
        {
            workspace.Replace(FirstJob, oldText, newText);
        }

        return ContentPipeline.Run(workspace.ContentRoot);
    }

    private static JsonElement Definition(ContentPackage package, string file, string id)
    {
        using var document = JsonDocument.Parse(package.DataFiles.Single(candidate => candidate.Path == file).Content);
        return document.RootElement.GetProperty("definitions")
            .EnumerateArray()
            .Single(definition => definition.GetProperty("id").GetString() == id)
            .Clone();
    }

    private static void AssertReports(ContentPipelineResult result, string file, string field, string message)
    {
        Assert.That(
            result.Diagnostics.Any(diagnostic =>
                diagnostic.File == file && diagnostic.FieldPath == field && diagnostic.Message.Contains(message)),
            Is.True,
            Describe(result));
    }

    [TestCase("  startingStats: { str: 5, agi: 5, vit: 5, int: 5, dex: 5, luk: 5 }\n", "server.startingStats")]
    [TestCase("  startingMap: map.training_ground\n", "server.startingMap")]
    [TestCase("  experienceTable: experience.adventurer\n", "server.experienceTable")]
    [TestCase("  basicAttack: skill.basic_attack\n", "server.basicAttack")]
    [TestCase("  movement:\n    baseSpeed: 5.0\n", "server.movement")]
    [TestCase("  unarmedAttackSpeedPenalty: 44\n", "server.unarmedAttackSpeedPenalty")]
    public void Run_WhenAFirstJobAuthorsAnInheritedField_ReportsIt(string field, string fieldPath)
    {
        ContentPipelineResult result = WithFirstJob("  weapons: [sword]\n", $"  weapons: [sword]\n{field}");

        AssertReports(result, FirstJob, fieldPath, "is taken from the base job, so a first job omits it");
    }

    [TestCase("baseJob: job.adventurer", "baseJob: job.missing", "references unknown job 'job.missing'")]
    [TestCase("baseJob: job.adventurer", "baseJob: job.vanguard", "which has a base job of its own")]
    public void Run_WhenTheBaseJobIsUnknownOrAFirstJob_ReportsIt(string oldText, string newText, string message)
    {
        ContentPipelineResult result = WithFirstJob(oldText, newText);

        AssertReports(result, FirstJob, "server.baseJob", message);
    }

    // A skill list carries 11 skills (Network Protocol §9), and a first job's list names its whole tree.
    [TestCase(10, true)]
    [TestCase(11, false)]
    public void Run_WhenAFirstJobsWholeTreePassesElevenSkills_ReportsIt(int ownSkills, bool isValid)
    {
        using var workspace = new ContentWorkspace();
        var own = Enumerable.Range(0, ownSkills)
            .Select(index => string.Format(CultureInfo.InvariantCulture, "skill.drill_{0}", index))
            .ToList();
        foreach (string skill in own)
        {
            workspace.Write(
                $"skills/{skill.Substring("skill.".Length)}.yml",
                workspace.Read("skills/focus.yml")
                    .Replace("id: skill.focus", $"id: {skill}", StringComparison.Ordinal));
        }

        workspace.Write(FirstJob, FirstJobText.Replace("[skill.focus]", $"[{string.Join(", ", own)}]",
            StringComparison.Ordinal));

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        if (isValid)
        {
            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        }
        else
        {
            AssertReports(result, FirstJob, "server.skills", "a whole tree of 12 skills");
        }
    }

    [TestCase("  weapons: [sword]\n", "", "server.weapons", "required field is missing")]
    [TestCase("weapons: [sword]", "weapons: []", "server.weapons", "at least one weapon type")]
    [TestCase("weapons: [sword]", "weapons: [sword, sword]", "server.weapons[1]", "more than once")]
    [TestCase("weapons: [sword]", "weapons: [axe]", "server.weapons[0]", "must be one of: sword, staff")]
    public void Run_WhenAJobsWeaponsAreBroken_ReportsThem(string oldText, string newText, string field, string message)
    {
        ContentPipelineResult result = WithFirstJob(oldText, newText);

        AssertReports(result, FirstJob, field, message);
    }

    // Another player may only be healed: there is no PvP (Gameplay Systems §6, §9).
    [TestCase(true)]
    [TestCase(false)]
    public void Run_ForAnAllySkill_AcceptsOnlyAHeal(bool isHeal)
    {
        using var workspace = new ContentWorkspace();
        string text = isHeal
            ? HealText
            : HealText.Replace("heal: { hp: 40 }", "damage: { ratio: 100 }", StringComparison.Ordinal)
                .Replace("targetType: ally", "targetType: ally\ndamageType: magical", StringComparison.Ordinal);
        workspace.Write("skills/mend.yml", text);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        if (isHeal)
        {
            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            Assert.That(
                Definition(result.Packages!.Client, "skills.json", "skill.mend").GetProperty("targetType")
                    .GetString(),
                Is.EqualTo("ally"));
        }
        else
        {
            AssertReports(result, "skills/mend.yml", "targetType", "may be ally only for a heal effect");
        }
    }

    // What a skill flies is presentation, so its key reaches the client package (Content Pipeline §5).
    [TestCase("projectile_arcane_bolt", true)]
    [TestCase("Arcane Bolt", false)]
    public void Build_WithASkillsProjectile_WritesItsKeyForTheClient(string key, bool isValid)
    {
        using var workspace = new ContentWorkspace();
        workspace.Replace("skills/strike.yml", "  icon: skill_strike\n",
            $"  icon: skill_strike\n  projectile: {key}\n");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        if (isValid)
        {
            Assert.That(result.Packages, Is.Not.Null, Describe(result));
            Assert.That(
                Definition(result.Packages!.Client, "skills.json", "skill.strike").GetProperty("projectile")
                    .GetString(),
                Is.EqualTo(key));
            Assert.That(
                Definition(result.Packages.Server, "skills.json", "skill.strike").TryGetProperty("projectile", out _),
                Is.False,
                "the server has no use for it");
        }
        else
        {
            AssertReports(result, "skills/strike.yml", "client.projectile", string.Empty);
        }
    }

    // An NPC that changes jobs offers every first job, 134 bytes each at the longest, beside its header of 14 bytes,
    // the 74 of each item it trades, and the 154 of its quest: with nine items more the Quartermaster trades eleven in
    // 982 bytes, which fit, and a first job's change brings them to 1,116.
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void Run_ForAnNpcThatChangesJobs_CountsEachFirstJobInItsServices(bool isChanging, bool isValid)
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(FirstJob, FirstJobText);
        for (int index = 0; index < 9; index++)
        {
            workspace.Write(
                string.Format(CultureInfo.InvariantCulture, "items/extra_{0}.yml", index),
                workspace.Read("items/slime_gel.yml").Replace(
                    "id: item.material.slime_gel",
                    string.Format(CultureInfo.InvariantCulture, "id: item.material.extra_{0}", index),
                    StringComparison.Ordinal));
        }

        workspace.Replace(
            Npc,
            "      price: 146443\n",
            $"      price: 146443\n  guild:\n    reset: false\n    jobChange: {(isChanging ? "true" : "false")}\n");

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        if (isValid)
        {
            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            Assert.That(Definition(result.Packages!.Server, "npcs.json", "npc.quartermaster")
                .GetProperty("jobChange").GetBoolean(), Is.False);
        }
        else
        {
            AssertReports(result, Npc, "id", "offers services that take 1116 bytes");
        }
    }

    // The server package carries a first job as authored, and its loader resolves it again; the client package keeps
    // a job's name and prefab alone (Content Pipeline §5).
    [Test]
    public void Build_ForAFirstJob_WritesItAsAuthoredForTheServerAndItsNameForTheClient()
    {
        ContentPipelineResult result = WithFirstJob();

        Assert.That(result.Packages, Is.Not.Null, Describe(result));
        JsonElement server = Definition(result.Packages!.Server, "jobs.json", "job.vanguard");
        Assert.That(
            server.EnumerateObject().Select(property => property.Name),
            Is.EqualTo(
                new[]
                {
                    "id", "displayName", "baseJob", "healthBase", "healthPerLevel", "spiritBase", "spiritPerLevel",
                    "jobExperienceTable", "skills", "weapons"
                }));
        Assert.That(server.GetProperty("baseJob").GetString(), Is.EqualTo("job.adventurer"));
        Assert.That(server.GetProperty("skills").EnumerateArray().Select(skill => skill.GetString()),
            Is.EqualTo(new[] { "skill.focus" }));
        Assert.That(server.GetProperty("weapons").EnumerateArray().Select(weapon => weapon.GetString()),
            Is.EqualTo(new[] { "sword" }));
        JsonElement baseJob = Definition(result.Packages.Server, "jobs.json", "job.adventurer");
        Assert.That(baseJob.GetProperty("weapons").EnumerateArray().Select(weapon => weapon.GetString()),
            Is.EqualTo(new[] { "sword", "staff" }));
        JsonElement client = Definition(result.Packages.Client, "jobs.json", "job.vanguard");
        Assert.That(
            client.EnumerateObject().Select(property => property.Name),
            Is.EqualTo(new[] { "id", "displayName", "prefab" }));
    }

    [Test]
    public void Run_ForAFirstJob_TakesItsBaseJobsValuesAndLearnsFromTheWholeTree()
    {
        ContentPipelineResult result = WithFirstJob();

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        JobDefinition adventurer = result.Content.Jobs.Single(job => job.Definition.Id.Value == "job.adventurer")
            .Definition;
        JobDefinition vanguard = result.Content.Jobs.Single(job => job.Definition.Id.Value == "job.vanguard")
            .Definition;
        Assert.That(vanguard.BaseJob, Is.EqualTo(adventurer.Id));
        Assert.That(
            (vanguard.StartingStats, vanguard.StartingMap, vanguard.ExperienceTable, vanguard.BasicAttack,
                vanguard.BaseSpeed, vanguard.UnarmedAttackSpeedPenalty),
            Is.EqualTo(
                (adventurer.StartingStats, adventurer.StartingMap, adventurer.ExperienceTable, adventurer.BasicAttack,
                    adventurer.BaseSpeed, adventurer.UnarmedAttackSpeedPenalty)));
        Assert.That((vanguard.HealthPerLevel, vanguard.SpiritPerLevel), Is.EqualTo((14, 3)), "its own values");
        Assert.That(
            vanguard.Tree.Select(skill => skill.Value),
            Is.EqualTo(new[] { "skill.strike", "skill.focus" }),
            "the base tree, then its own");
        Assert.That(vanguard.CarriedSkillPoints, Is.EqualTo(2), "a table of two caps the base job at 3");
        Assert.That(vanguard.Weapons, Is.EqualTo(new[] { WeaponType.Sword }));
        Assert.That(adventurer.Weapons, Is.EqualTo(new[] { WeaponType.Sword, WeaponType.Staff }));
    }

    // The repository's first jobs (Gameplay Systems §2.1, §9, §9.1, §11.1): the Vanguard and the Arcanist, each the
    // Adventurer's first job with 9 points carried, their six skills, the shared table of 4,050, the weapons, the
    // statuses, and the Guildmaster's job change.
    [Test]
    public void Run_ForRepositoryContent_GivesTheFirstJobsTheirTreesSkillsAndWeapons()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));

        Assert.That(result.Diagnostics, Is.Empty, Describe(result));

        JobDefinition Job(string id)
        {
            return result.Content.Jobs.Single(job => job.Definition.Id.Value == id).Definition;
        }

        SkillDefinition Skill(string id)
        {
            return result.Content.Skills.Single(skill => skill.Definition.Id.Value == id).Definition;
        }

        string[] adventurerTree = { "skill.strike", "skill.first_aid", "skill.focus" };
        Assert.That(
            Job("job.vanguard").Tree.Select(skill => skill.Value),
            Is.EqualTo(adventurerTree.Concat(new[] { "skill.heavy_blow", "skill.war_cry", "skill.iron_guard" })));
        Assert.That(
            Job("job.arcanist").Tree.Select(skill => skill.Value),
            Is.EqualTo(adventurerTree.Concat(new[] { "skill.arcane_bolt", "skill.clarity", "skill.mend" })));
        Assert.That(
            new[] { "job.vanguard", "job.arcanist" }.Select(id => (Job(id).BaseJob?.Value, Job(id).CarriedSkillPoints,
                Job(id).JobExperienceTable.Value)),
            Is.All.EqualTo(("job.adventurer", 9, "experience.first_job")));
        Assert.That(
            (Job("job.vanguard").HealthPerLevel, Job("job.vanguard").SpiritPerLevel, Job("job.arcanist").HealthPerLevel,
                Job("job.arcanist").SpiritPerLevel),
            Is.EqualTo((14, 3, 5, 7)));
        Assert.That(
            (Job("job.adventurer").Weapons.Count, Job("job.vanguard").Weapons.Single(),
                Job("job.arcanist").Weapons.Single()),
            Is.EqualTo((2, WeaponType.Sword, WeaponType.Staff)));
        Assert.That(
            result.Content.ExperienceTables.Single(table => table.Definition.Id.Value == "experience.first_job")
                .Definition.Levels.Sum(),
            Is.EqualTo(4050));

        SkillDefinition heavyBlow = Skill("skill.heavy_blow");
        Assert.That(
            heavyBlow.Levels.Select(level => (level.Effect.DamageRatioPercent, level.SpCost)),
            Is.EqualTo(new[] { (200, 12), (225, 14), (250, 16), (275, 18), (300, 20) }));
        Assert.That(
            heavyBlow.Levels.Select(level => (level.AfterCastDelayMs, level.CooldownMs)),
            Is.All.EqualTo((800, 4000)));
        SkillDefinition arcaneBolt = Skill("skill.arcane_bolt");
        Assert.That((arcaneBolt.DamageType, arcaneBolt.Range), Is.EqualTo((SkillDamageType.Magical, 8.0)));
        Assert.That(
            arcaneBolt.Levels.Select(level => (level.Effect.DamageRatioPercent, level.SpCost)),
            Is.EqualTo(new[] { (100, 10), (125, 12), (150, 14), (175, 16), (200, 18) }));
        Assert.That(
            arcaneBolt.Levels.Select(level => (level.FixedCastMs, level.VariableCastMs, level.AfterCastDelayMs)),
            Is.All.EqualTo((300, 800, 500)));
        Assert.That(
            result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.arcane_bolt").Projectile,
            Is.EqualTo("projectile_arcane_bolt"));
        SkillDefinition mend = Skill("skill.mend");
        Assert.That((mend.TargetType, mend.Range), Is.EqualTo((SkillTargetType.Ally, 6.0)));
        Assert.That(
            mend.Levels.Select(level => (level.Effect.HealHp, level.SpCost, level.FixedCastMs)),
            Is.EqualTo(new[] { (40, 12, 1000), (70, 15, 1000), (100, 18, 1000) }));
        foreach ((string skill, string status, string requires) in new[]
                 {
                     ("skill.war_cry", "status.war_cry", "skill.heavy_blow"),
                     ("skill.iron_guard", "status.iron_guard", "skill.war_cry"),
                     ("skill.clarity", "status.clarity", "skill.arcane_bolt"),
                     ("skill.mend", string.Empty, "skill.clarity")
                 })
        {
            Assert.That(Skill(skill).Requires!.Skill.Value, Is.EqualTo(requires), skill);
            if (status.Length > 0)
            {
                Assert.That(
                    Skill(skill).Levels.Select(level =>
                        (level.Effect.Status.Value, level.Effect.StatusDurationMs, level.SpCost)),
                    Is.All.EqualTo((status, 60_000, 15)),
                    skill);
            }
        }

        Assert.That(
            Skill("skill.war_cry").Levels.Select(level => level.Effect.StatPercent.Str),
            Is.EqualTo(new[] { 40, 70, 100 }));
        Assert.That(
            Skill("skill.iron_guard").Levels.Select(level => level.Effect.StatPercent.Vit),
            Is.EqualTo(new[] { 40, 70, 100 }));
        Assert.That(
            Skill("skill.clarity").Levels.Select(level => level.Effect.StatPercent.Int),
            Is.EqualTo(new[] { 40, 70, 100 }));
        Assert.That(
            result.Content.Npcs.Single(npc => npc.Definition.Id.Value == "npc.guildmaster").Definition
                .OffersJobChange,
            Is.True);
        Assert.That(
            result.Content.Items.Where(item => item.Definition.Equipment?.WeaponType != null)
                .Select(item => (item.Definition.Id.Value, item.Definition.Equipment!.WeaponType)),
            Is.EquivalentTo(
                new (string, WeaponType?)[]
                {
                    ("item.weapon.training_sword", WeaponType.Sword), ("item.weapon.training_staff", WeaponType.Staff)
                }));
    }

    [Test]
    public void Run_WhenAFirstJobRepeatsASkillOfItsBaseTree_ReportsIt()
    {
        ContentPipelineResult result = WithFirstJob("skills: [skill.focus]", "skills: [skill.focus, skill.strike]");

        AssertReports(result, FirstJob, "server.skills[1]", "repeats 'skill.strike' of its base job's tree");
    }

    // A prerequisite may name any skill of the whole tree, so a first job's skill may require a base one, but a base
    // job's skill may not require a first job's.
    [Test]
    public void Run_WhenAPrerequisiteCrossesTheWholeTree_AcceptsItOnlyFromTheFirstJob()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(FirstJob, FirstJobText);
        workspace.Replace("skills/focus.yml", "  range: 0\n",
            "  range: 0\n  requires: { skill: skill.strike, level: 1 }\n");
        ContentPipelineResult valid = ContentPipeline.Run(workspace.ContentRoot);
        workspace.Replace("skills/focus.yml", "requires: { skill: skill.strike, level: 1 }", string.Empty);
        workspace.Replace("skills/strike.yml", "  range: 1.6875\n",
            "  range: 1.6875\n  requires: { skill: skill.focus, level: 1 }\n");
        ContentPipelineResult invalid = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(valid.Diagnostics, Is.Empty, Describe(valid));
        AssertReports(invalid, BaseJob, "server.skills[0]", "requires 'skill.focus', not another of the tree");
    }

    // A base job refused for its own problem is reported there alone (§7).
    [Test]
    public void Run_WhenTheBaseJobIsRefusedForItsOwnProblem_ReportsNothingOnTheFirstJob()
    {
        using var workspace = new ContentWorkspace();
        workspace.Write(FirstJob, FirstJobText);
        workspace.Replace(BaseJob, "  weapons: [sword, staff]\n", string.Empty);

        ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.File), Is.EqualTo(new[] { BaseJob }),
            Describe(result));
    }
}
}
