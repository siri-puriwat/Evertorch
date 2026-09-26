using System;
using System.IO;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
[TestFixture]
public sealed class ContentValidationTests
{
    private const string Item = "items/slime_gel.yml";
    private const string Potion = "items/minor_health.yml";
    private const string Monster = "monsters/training_slime.yml";
    private const string Skill = "skills/basic_attack.yml";
    private const string Job = "jobs/adventurer.yml";
    private const string Map = "maps/training_ground.yml";
    private const string PortalCenter = "center: { x: 6.5, y: 0.0, z: -2.5 }";
    private const string Arrival = "position: { x: -3.5, y: 0.0, z: 3.5 }";
    private const string Experience = "experience/adventurer.yml";
    private const string Strike = "skills/strike.yml";
    private const string Focus = "skills/focus.yml";
    private const string FocusStatus = "status-effects/focus.yml";
    private const string JobSkills = "skills: [skill.strike]";
    private const string Levels = "levels: [30211, 50423, 80637]";
    private const string SlimeCenter = "center: { x: 12.6875, y: 0.0, z: 14.3125 }";

    [TestCase(Potion, "id: item.consumable.minor_health", "id: Item.Consumable.MinorHealth", "id", "not a valid ID")]
    [TestCase(Potion, "id: item.consumable.minor_health", "id: item..minor_health", "id", "not a valid ID")]
    [TestCase(Potion, "id: item.consumable.minor_health", "id: monster.minor_health", "id", "expected 'item.'")]
    [TestCase(
        Potion,
        "id: item.consumable.minor_health",
        "id: item.consumable.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "id",
        "64 characters at most")]
    [TestCase(Item, "stackLimit: 999\n", "", "stackLimit", "required field is missing")]
    [TestCase(Item, "stackLimit: 999", "stackLimit: 12.5", "stackLimit", "whole number")]
    [TestCase(Item, "stackLimit: 999", "stackLimit: \"999\"", "stackLimit", "whole number")]
    [TestCase(Item, "stackLimit: 999", "stackLimit: 0", "stackLimit", "between 1 and")]
    [TestCase(Item, "type: material", "type: weapon", "type", "one of: material, consumable")]
    [TestCase(Item, "type: material", "type: material\nrarity: common", "rarity", "unknown field")]
    [TestCase(Item, "  weight: 3917", "  weight: 3917\n  secret: 1", "server.secret", "unknown field")]
    [TestCase(Item, "  sellPrice: 73219", "  sellPrice: -1", "server.sellPrice", "between 0 and")]
    [TestCase(Item, "displayName: Slime Gel", "displayName: \"\"", "displayName", "must not be empty")]
    [TestCase(Item, "icon: item_slime_gel", "icon: Assets/Icons/slime_gel.png", "client.icon", "logical asset key")]
    [TestCase(Item, "server:\n  weight: 3917\n  sellPrice: 73219", "server: 5", "server", "must be a mapping")]
    [TestCase(Monster, "chance: 0.7321", "chance: -0.1", "drops[0].chance", "between 0 and 1")]
    [TestCase(Monster, "chance: 0.7321", "chance: 1.1", "drops[0].chance", "between 0 and 1")]
    [TestCase(Monster, "chance: 0.7321", "chance: .nan", "drops[0].chance", "finite number")]
    [TestCase(Monster, "{ min: 1, max: 2 }", "{ min: 3, max: 2 }", "drops[0].amount.min", "greater than max")]
    [TestCase(Monster, "{ min: 1, max: 2 }", "{ min: 0, max: 2 }", "drops[0].amount.min", "between 1 and")]
    [TestCase(
        Monster,
        "{ min: 1, max: 2 }",
        "{ min: 1, max: 1000 }",
        "drops[0].amount.max",
        "exceeds the stack limit 999 of item 'item.material.slime_gel'")]
    [TestCase(Monster, "attackIntervalMs: 12347", "attackIntervalMs: 0", "combat.attackIntervalMs", "between 1 and")]
    [TestCase(Monster, "scanIntervalMs: 139", "scanIntervalMs: 0", "ai.scanIntervalMs", "between 1 and")]
    [TestCase(Monster, "roamRadius: 6.4375", "roamRadius: -1", "ai.roamRadius", "between 0 and")]
    [TestCase(Monster, "{ min: 4111, max: 5227 }", "{ min: 5228, max: 5227 }", "ai.idlePauseMs.min",
        "greater than max")]
    [TestCase(Monster, "{ min: 4111, max: 5227 }", "{ min: -1, max: 5227 }", "ai.idlePauseMs.min", "between 0 and")]
    [TestCase(Monster, "attackRange: 1.5625", "attackRange: 0", "combat.attackRange", "greater than 0")]
    [TestCase(Monster, "hp: 54321", "hp: 0", "stats.hp", "between 1 and")]
    [TestCase(Monster, "level: 1", "level: 0", "level", "between 1 and")]
    [TestCase(Monster, "behavior: passive", "behavior: sleepy", "ai.behavior", "one of: passive, aggressive")]
    [TestCase(
        Monster,
        "item: item.material.slime_gel",
        "item: item.material.missing",
        "drops[0].item",
        "references unknown item 'item.material.missing'")]
    [TestCase(
        Monster,
        "item: item.material.slime_gel",
        "item: monster.training_slime",
        "drops[0].item",
        "expected 'item.'")]
    [TestCase(
        Map,
        "monster: monster.training_slime",
        "monster: monster.missing",
        "server.monsterSpawns[0].monster",
        "references unknown monster 'monster.missing'")]
    [TestCase(Map, "count: 4", "count: 0", "server.monsterSpawns[0].count", "between 1 and")]
    [TestCase(
        Map,
        "facing: { x: 0.0, z: 1.0 }",
        "facing: { x: 0.0, z: 0.0 }",
        "server.spawnPoint.facing",
        "zero direction")]
    [TestCase(Map, "x: 3.4375", "x: north", "server.spawnPoint.position.x", "finite number")]
    [TestCase(
        Job,
        "startingMap: map.training_ground",
        "startingMap: map.missing",
        "server.startingMap",
        "references unknown map 'map.missing'")]
    [TestCase(
        Job,
        "basicAttack: skill.basic_attack",
        "basicAttack: skill.missing",
        "server.basicAttack",
        "references unknown skill 'skill.missing'")]
    [TestCase(
        Job,
        "experienceTable: experience.adventurer",
        "experienceTable: experience.missing",
        "server.experienceTable",
        "references unknown experience table 'experience.missing'")]
    [TestCase(
        Job,
        "  experienceTable: experience.adventurer\n",
        "",
        "server.experienceTable",
        "required field is missing")]
    [TestCase(Job, "str: 5", "str: -1", "server.startingStats.str", "between 0 and")]
    [TestCase(Job, "baseSpeed: 5.1875", "baseSpeed: 0", "server.movement.baseSpeed", "greater than 0")]
    [TestCase(Skill, "targetType: enemy", "targetType: everyone", "targetType", "one of: enemy, self")]
    [TestCase(
        Strike,
        "    damage: { ratio: 1319 }",
        "    damage: { ratio: 1319 }\n    heal: { hp: 5 }",
        "server.effect.damage",
        "exactly one of damage, heal, or status")]
    [TestCase(Focus, "targetType: self", "targetType: enemy", "targetType", "must be self for a status effect")]
    [TestCase(
        Focus,
        "status: status.focus",
        "status: status.missing",
        "server.effect.status.status",
        "references unknown status effect 'status.missing'")]
    [TestCase(Focus, "durationMs: 43117", "durationMs: 0", "server.effect.status.durationMs", "between 1 and")]
    [TestCase(Focus, "status: status.focus", "status: skill.focus", "server.effect.status.status",
        "expected 'status.'")]
    [TestCase(FocusStatus, "agi: 137", "agi: 1001", "server.statPercent.agi", "between 0 and")]
    [TestCase(FocusStatus, "agi: 137", "agi: -1", "server.statPercent.agi", "between 0 and")]
    [TestCase(FocusStatus, "displayName: Focus\n", "", "displayName", "required field is missing")]
    [TestCase(FocusStatus, "icon: status_focus", "icon: Status/Focus.png", "client.icon", "logical asset key")]
    [TestCase(Strike, "ratio: 1319", "ratio: 0", "server.effect.damage.ratio", "between 1 and")]
    [TestCase(Strike, "damageType: physical\n", "", "damageType", "required for a damage effect")]
    [TestCase(Strike, "spPaidAt: castStart", "spPaidAt: later", "server.spPaidAt", "one of: resolution, castStart")]
    [TestCase(Strike, "cooldownMs: 21133", "cooldownMs: -1", "server.cooldownMs", "between 0 and")]
    [TestCase(Strike, "fixed: 3171", "fixed: 1.5", "server.castTimeMs.fixed", "whole number")]
    [TestCase(
        Job,
        JobSkills,
        "skills: [skill.missing]",
        "server.skills[0]",
        "references unknown skill 'skill.missing'")]
    [TestCase(Job, JobSkills, "skills: [skill.basic_attack]", "server.skills[0]", "which has no effect")]
    [TestCase(Job, JobSkills, "skills: [skill.strike, skill.strike]", "server.skills[1]", "more than once")]
    [TestCase(Job, JobSkills, "skills: [Skill.Strike]", "server.skills[0]", "not a valid ID")]
    [TestCase(Experience, Levels, "levels: [30211, 0, 80637]", "levels[1]", "between 1 and")]
    [TestCase(Experience, Levels, "levels: [30211, 1.5, 80637]", "levels[1]", "whole number")]
    [TestCase(Experience, Levels, "levels: [30211, \"50423\", 80637]", "levels[1]", "whole number")]
    [TestCase(Experience, Levels, "levels: 30211", "levels", "must be a sequence")]
    [TestCase(Experience, Levels, "levels: []", "levels", "must list between 1 and 998 levels")]
    [TestCase(Experience, Levels, Levels + "\ncap: 4", "cap", "unknown field")]
    [TestCase(Monster, "baseExperience: 77173", "baseExperience: -1", "rewards.baseExperience", "between 0 and")]
    [TestCase(Monster, "rewards:\n  baseExperience: 77173", "rewards: {}", "rewards.baseExperience", "required field")]
    [TestCase(Monster, "rewards:\n  baseExperience: 77173", "rewards: 77173", "rewards", "must be a mapping")]
    [TestCase(
        Map,
        "        map: map.training_ground",
        "        map: map.elsewhere",
        "server.portals[0].destination.map",
        "references unknown map 'map.elsewhere'")]
    [TestCase(
        Map,
        Arrival,
        "position: { x: -99.0, y: 0.0, z: 3.5 }",
        "server.portals[0].destination.position",
        "is not a place to stand on the destination map")]
    [TestCase(
        Map,
        Arrival,
        "position: { x: 6.5, y: 0.0, z: -2.5 }",
        "server.portals[0].destination.position",
        "lies inside a portal of the destination map")]
    [TestCase(
        Map,
        PortalCenter,
        "center: { x: -99.0, y: 0.0, z: -2.5 }",
        "server.portals[0].center",
        "is not a place the navigation grid lets an agent stand")]
    [TestCase(
        Map,
        PortalCenter,
        "center: { x: 3.4375, y: 0.0, z: -7.5625 }",
        "server.spawnPoint.position",
        "lies inside a portal")]
    [TestCase(Map, "radius: 1.1875", "radius: 0", "server.portals[0].radius", "greater than 0")]
    [TestCase(Monster, "scanIntervalMs: 139", "scanIntervalMs: 139\n  keepDistance: 1.5625", "ai.keepDistance",
        "must be below combat.attackRange")]
    [TestCase(Monster, "hit: 1553", "hit: 1553\n  magicAttack: -1", "stats.magicAttack", "between 0 and")]
    [TestCase(
        Monster,
        "icon: monster_training_slime_icon",
        "icon: monster_training_slime_icon\n  projectile: Projectiles/Spark.prefab",
        "client.projectile",
        "logical asset key")]
    [TestCase(Monster, "drops:", "skills:\n  - skill: skill.strike\n    chance: 1.5\ndrops:", "skills[0].chance",
        "between 0 and 1")]
    [TestCase(Monster, "drops:", "skills:\n  - skill: skill.none\n    chance: 0.5\ndrops:", "skills[0].skill",
        "references unknown skill 'skill.none'")]
    [TestCase(Monster, "drops:", "skills:\n  - skill: skill.basic_attack\n    chance: 0.5\ndrops:",
        "skills[0].skill", "which has no effect")]
    [TestCase(
        Monster,
        "drops:",
        "skills:\n  - skill: skill.strike\n    chance: 0.5\n  - skill: skill.strike\n    chance: 0.5\ndrops:",
        "skills[1].skill",
        "more than once")]
    [TestCase(
        Map,
        "facing: { x: 0.0, z: -1.0 }",
        "facing: { x: 0.0, z: 0.0 }",
        "server.portals[0].destination.facing",
        "must not be the zero direction")]
    public void Run_WhenOneFieldIsBroken_ReportsThatFileFieldAndLine(
        string file,
        string oldText,
        string newText,
        string expectedFieldPath,
        string expectedMessagePart)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(file, oldText, newText);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Packages, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            ContentDiagnostic diagnostic = result.Diagnostics[0];
            Assert.That(diagnostic.File, Is.EqualTo(file));
            Assert.That(diagnostic.FieldPath, Is.EqualTo(expectedFieldPath));
            Assert.That(diagnostic.Message, Does.Contain(expectedMessagePart));
            Assert.That(diagnostic.Line, Is.GreaterThan(0));
        }
    }

    // Made aggressive, the slime's spawn must lie farther than max(6.875, 6.4375) + 6.125 + 1 = 14 m from the spawn
    // point (3.4375, 0, -7.5625) and from the arrival (-3.5, 0, 3.5) of the map's own portal; 14 m is not farther.
    [TestCase("center: { x: 8.75, y: 0.0, z: -16.0 }", "the spawn point")]
    [TestCase("center: { x: 17.4375, y: 0.0, z: -7.5625 }", "the spawn point")]
    [TestCase("center: { x: -7.5, y: 0.0, z: 13.5 }", "the arrival from map.training_ground")]
    public void Run_WhenAnAggressiveSpawnCouldPerceiveAnArrival_ReportsItsCenter(string center, string arrival)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Monster, "behavior: passive", "behavior: aggressive");
            workspace.Replace(Map, SlimeCenter, center);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            ContentDiagnostic diagnostic = result.Diagnostics[0];
            Assert.That((diagnostic.File, diagnostic.FieldPath), Is.EqualTo((Map, "server.monsterSpawns[0].center")));
            Assert.That(diagnostic.Message, Does.Contain($"not farther than 14 m from {arrival}"));
        }
    }

    [TestCase("behavior: aggressive", "center: { x: 17.5, y: 0.0, z: -7.5625 }")]
    [TestCase("behavior: passive", "center: { x: 8.75, y: 0.0, z: -16.0 }")]
    public void Run_WhenASpawnIsOutOfReachOrItsMonsterPassive_IsValid(string behavior, string center)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Monster, "behavior: passive", behavior);
            workspace.Replace(Map, SlimeCenter, center);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        }
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("1.0")]
    [TestCase("0.5")]
    public void Run_WhenChanceIsOnOrInsideTheBounds_IsValid(string chance)
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Monster, "chance: 0.7321", $"chance: {chance}");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        }
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Evertorch.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Evertorch.sln was not found above the test directory.");
        return directory!.FullName;
    }

    private static string Describe(ContentPipelineResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
    }

    [TestCase(998, true)]
    [TestCase(999, false)]
    public void Run_ForAnExperienceTable_AcceptsAtMostOneLevelBelowTheLevelLimit(int levels, bool isValid)
    {
        using (var workspace = new ContentWorkspace())
        {
            string table = string.Join(", ", Enumerable.Repeat("10", levels));
            workspace.Replace(Experience, Levels, $"levels: [{table}]");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Packages != null, Is.EqualTo(isValid), Describe(result));
            Assert.That(
                result.Diagnostics.Select(diagnostic => diagnostic.Message),
                isValid ? Is.Empty : Is.EqualTo(new[] { "must list between 1 and 998 levels" }));
        }
    }

    [Test]
    public void Run_ForASkillWithoutOptionalNumbers_TakesThemAsZeroAndPaysAtResolution()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(
                Strike,
                "  spCost: 4127\n  spPaidAt: castStart\n  castTimeMs: { fixed: 3171, variable: 7193 }\n"
                + "  afterCastDelayMs: 5231\n  cooldownMs: 21133\n",
                "");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            SkillDefinition strike = result.Content.Skills.Single(skill => skill.Definition.Id.Value == "skill.strike")
                .Definition;
            Assert.That(
                (strike.SpCost, strike.SpPaidAt, strike.FixedCastMs, strike.VariableCastMs, strike.AfterCastDelayMs,
                    strike.CooldownMs),
                Is.EqualTo((0, SkillPaymentPoint.Resolution, 0, 0, 0, 0)));
        }
    }

    [Test]
    public void Run_ForRepositoryContent_HasNoDiagnostics()
    {
        string contentRoot = Path.Combine(RepositoryRoot(), "content");

        ContentPipelineResult result = ContentPipeline.Run(contentRoot);

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.ToString()), Is.Empty);
        Assert.That(result.Content.Maps.Select(map => map.Definition.Id.Value), Does.Contain("map.training_ground"));
        Assert.That(result.Content.Jobs.Select(job => job.Definition.Id.Value), Does.Contain("job.adventurer"));
        Assert.That(result.Content.Monsters.Select(monster => monster.Definition.Id.Value),
            Does.Contain("monster.training_slime"));
        Assert.That(
            result.Content.Items.Select(item => item.Definition.Id.Value),
            Does.Contain("item.material.slime_gel"));
        Assert.That(
            result.Content.Skills.Select(skill => skill.Definition.Id.Value),
            Does.Contain("skill.basic_attack"));
    }

    [Test]
    public void Run_ForValidFixture_HasNoDiagnosticsAndBuildsPackages()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Packages, Is.Not.Null);
            Assert.That(result.Content.Items, Has.Count.EqualTo(2));
            Assert.That(result.Content.Monsters, Has.Count.EqualTo(1));
            Assert.That(result.Content.Skills, Has.Count.EqualTo(3));
            Assert.That(result.Content.Jobs, Has.Count.EqualTo(1));
            Assert.That(result.Content.Maps, Has.Count.EqualTo(1));
            Assert.That(result.Content.ExperienceTables, Has.Count.EqualTo(1));
            Assert.That(result.Content.StatusEffects, Has.Count.EqualTo(1));
            Assert.That(
                result.Content.ExperienceTables[0].Definition.Levels,
                Is.EqualTo(new[] { 30211, 50423, 80637 }));
        }
    }

    [Test]
    public void Run_WhenAJobListsMoreSkillsThanASkillListCarries_ReportsTheSkills()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Job, JobSkills,
                "skills: [" + string.Join(", ", Enumerable.Repeat("skill.strike", 12)) + "]");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Where(diagnostic => diagnostic.FieldPath == "server.skills")
                    .Select(diagnostic => diagnostic.Message),
                Is.EqualTo(new[] { "lists more than the 11 skills a skill list carries" }),
                Describe(result));
        }
    }

    [Test]
    public void Run_WhenAMonsterHasNoRewards_GivesItNoExperience()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Monster, "rewards:\n  baseExperience: 77173\n", "");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
            Assert.That(result.Content.Monsters.Single().Definition.BaseExperience, Is.Zero);
        }
    }

    [Test]
    public void Run_WhenAStatusEffectHasNoIcon_IsValid_AndMoreThanFourteenAreRefused()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(FocusStatus, "client:\n  icon: status_focus\n", "");
            ContentPipelineResult withoutIcon = ContentPipeline.Run(workspace.ContentRoot);
            for (int index = 1; index <= 14; index++)
            {
                workspace.Write(
                    $"status-effects/extra_{index:D2}.yml",
                    $"id: status.extra_{index:D2}\ndisplayName: Extra\nserver:\n  statPercent: {{ str: 1 }}\n");
            }

            ContentPipelineResult fifteen = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(withoutIcon.Diagnostics, Is.Empty, Describe(withoutIcon));
            Assert.That(fifteen.Diagnostics, Has.Count.EqualTo(1), Describe(fifteen));
            Assert.That(fifteen.Diagnostics[0].File, Is.EqualTo(FocusStatus), "the fifteenth by ID");
            Assert.That(fifteen.Diagnostics[0].Message, Does.Contain("more than the 14 status effects"));
        }
    }

    [Test]
    public void Run_WhenAmountMinEqualsMax_IsValid()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Monster, "{ min: 1, max: 2 }", "{ min: 2, max: 2 }");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Is.Empty, Describe(result));
        }
    }

    [Test]
    public void Run_WhenBrokenFieldIsOnAKnownLine_ReportsThatLine()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Item, "stackLimit: 999", "stackLimit: many");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics[0].Line, Is.EqualTo(4));
            Assert.That(
                result.Diagnostics[0].ToString(),
                Is.EqualTo("items/slime_gel.yml(4): stackLimit: must be a whole number"));
        }
    }

    [Test]
    public void Run_WhenContentDirectoryIsMissing_ReportsIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            ContentPipelineResult result = ContentPipeline.Run(Path.Combine(workspace.ContentRoot, "absent"));

            Assert.That(result.Packages, Is.Null);
            Assert.That(result.Diagnostics[0].Message, Is.EqualTo("content directory does not exist"));
        }
    }

    [Test]
    public void Run_WhenDropsIsNotASequence_ReportsTheField()
    {
        using (var workspace = new ContentWorkspace())
        {
            string text = workspace.Read(Monster);
            int start = text.IndexOf("drops:", StringComparison.Ordinal);
            int end = text.IndexOf("client:", StringComparison.Ordinal);
            workspace.Write(Monster, $"{text.Substring(0, start)}drops: none\n{text.Substring(end)}");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].FieldPath, Is.EqualTo("drops"));
            Assert.That(result.Diagnostics[0].Message, Is.EqualTo("must be a sequence"));
        }
    }

    [Test]
    public void Run_WhenFieldIsDeclaredTwice_ReportsInvalidYaml()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Item, "stackLimit: 999", "stackLimit: 999\nstackLimit: 5");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Packages, Is.Null);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.File), Does.Contain(Item));
        }
    }

    [Test]
    public void Run_WhenFileIsOutsideAKnownFolder_ReportsTheFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write("recipes/bread.yml", "id: recipe.bread\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].File, Is.EqualTo("recipes/bread.yml"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("known definition folder"));
        }
    }

    [Test]
    public void Run_WhenFileUsesAnotherExtension_ReportsTheFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Move(Skill, "skills/basic_attack.yaml");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Select(diagnostic => diagnostic.ToString()),
                Does.Contain("skills/basic_attack.yaml: content files must use the .yml extension"));
        }
    }

    [Test]
    public void Run_WhenMappingIsMissing_ReportsItOnceWithoutItsFields()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Item, "client:\n  icon: item_slime_gel\n  model: pickup_slime_gel\n", string.Empty);

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].FieldPath, Is.EqualTo("client"));
        }
    }

    [Test]
    public void Run_WhenOneFileHasSeveralProblems_ReportsAllOfThem()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Item, "stackLimit: 999", "stackLimit: many");
            workspace.Replace(Item, "sellPrice: 73219", "sellPrice: -5");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(
                result.Diagnostics.Where(diagnostic => diagnostic.File == Item)
                    .Select(diagnostic => diagnostic.FieldPath),
                Is.EquivalentTo(new[] { "stackLimit", "server.sellPrice" }));
        }
    }

    [Test]
    public void Run_WhenTheBasicAttackHasNoDamageType_ReportsTheJob()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace(Skill, "damageType: physical\n", "");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].File, Is.EqualTo(Job));
            Assert.That(result.Diagnostics[0].FieldPath, Is.EqualTo("server.basicAttack"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("without a damageType"));
        }
    }

    [Test]
    public void Run_WhenTwoExperienceTablesShareAnId_ReportsTheSecondFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write("experience/copy.yml", "id: experience.adventurer\nlevels: [5]\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].File, Is.EqualTo("experience/copy.yml"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("duplicate ID 'experience.adventurer'"));
        }
    }

    [Test]
    public void Run_WhenTwoFilesShareAnId_ReportsTheSecondFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write("items/slime_gel_copy.yml", workspace.Read(Item));

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            Assert.That(result.Diagnostics, Has.Count.EqualTo(1), Describe(result));
            Assert.That(result.Diagnostics[0].File, Is.EqualTo("items/slime_gel_copy.yml"));
            Assert.That(result.Diagnostics[0].FieldPath, Is.EqualTo("id"));
            Assert.That(result.Diagnostics[0].Line, Is.EqualTo(1));
            Assert.That(
                result.Diagnostics[0].Message,
                Is.EqualTo("duplicate ID 'item.material.slime_gel'; already defined in items/slime_gel.yml"));
        }
    }

    [Test]
    public void Run_WhenYamlIsMalformed_ReportsTheFile()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Write(Skill, "id: skill.basic_attack\ndisplayName: [unclosed\n");

            ContentPipelineResult result = ContentPipeline.Run(workspace.ContentRoot);

            ContentDiagnostic diagnostic = result.Diagnostics.First(candidate => candidate.File == Skill);
            Assert.That(diagnostic.FieldPath, Is.Empty);
            Assert.That(diagnostic.Message, Does.StartWith("invalid YAML"));
            Assert.That(diagnostic.Line, Is.GreaterThan(0));
        }
    }
}
}
