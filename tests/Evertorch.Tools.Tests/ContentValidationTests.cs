using System;
using System.IO;
using System.Linq;
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
    [TestCase(Monster, "attackIntervalMs: 12347", "attackIntervalMs: 0", "combat.attackIntervalMs", "between 1 and")]
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
    [TestCase(Job, "str: 5", "str: -1", "server.startingStats.str", "between 0 and")]
    [TestCase(Job, "baseSpeed: 5.1875", "baseSpeed: 0", "server.movement.baseSpeed", "greater than 0")]
    [TestCase(Skill, "targetType: enemy", "targetType: everyone", "targetType", "one of: enemy, self")]
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
            Assert.That(result.Content.Skills, Has.Count.EqualTo(1));
            Assert.That(result.Content.Jobs, Has.Count.EqualTo(1));
            Assert.That(result.Content.Maps, Has.Count.EqualTo(1));
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
