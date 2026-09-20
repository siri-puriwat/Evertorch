using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Tools;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class ServerContentLoaderTests
{
    private const string Manifest = "manifest.json";
    private const string Items = "items.json";
    private const string Jobs = "jobs.json";
    private const string Maps = "maps.json";
    private const string Monsters = "monsters.json";
    private const string Skills = "skills.json";

    [Test]
    public void Load_ForRepositoryContent_LoadsEveryDefinitionTheToolsWrote()
    {
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.RepositoryContentDirectory);

        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        Assert.That(content.ServerContentVersion, Is.EqualTo(pipeline.Packages!.Server.Version));
        Assert.That(content.ClientContentVersion, Is.EqualTo(pipeline.Packages.Client.Version));
        Assert.That(
            content.Items.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.Items.Select(item => item.Definition.Id.Value)));
        Assert.That(
            content.Monsters.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.Monsters.Select(monster => monster.Definition.Id.Value)));
        Assert.That(
            content.Skills.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.Skills.Select(skill => skill.Definition.Id.Value)));
        Assert.That(
            content.Jobs.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.Jobs.Select(job => job.Definition.Id.Value)));
        Assert.That(
            content.Maps.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.Maps.Select(map => map.Definition.Id.Value)));
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsTheSameValuesTheToolsParsed()
    {
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.RepositoryContentDirectory);
        JobDefinition authoredJob = pipeline.Content.Jobs.Single().Definition;
        MapDefinition authoredMap = pipeline.Content.Maps.Single().Definition;
        MonsterDefinition authoredMonster = pipeline.Content.Monsters.Single().Definition;

        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        JobDefinition job = content.Jobs[authoredJob.Id];
        Assert.That(job.StartingStats, Is.EqualTo(authoredJob.StartingStats));
        Assert.That(job.BaseSpeed, Is.EqualTo(authoredJob.BaseSpeed));
        Assert.That(job.StartingMap, Is.EqualTo(authoredJob.StartingMap));
        Assert.That(job.BasicAttack, Is.EqualTo(authoredJob.BasicAttack));
        Assert.That(job.UnarmedAttackSpeedPenalty, Is.EqualTo(authoredJob.UnarmedAttackSpeedPenalty));

        MapDefinition map = content.Maps[authoredMap.Id];
        Assert.That(map.SpawnPosition, Is.EqualTo(authoredMap.SpawnPosition));
        Assert.That(map.SpawnFacing, Is.EqualTo(authoredMap.SpawnFacing));
        Assert.That(map.MonsterSpawns, Has.Count.EqualTo(authoredMap.MonsterSpawns.Count));
        Assert.That(map.MonsterSpawns[0].Monster, Is.EqualTo(authoredMap.MonsterSpawns[0].Monster));
        Assert.That(map.MonsterSpawns[0].Center, Is.EqualTo(authoredMap.MonsterSpawns[0].Center));
        Assert.That(map.MonsterSpawns[0].RespawnMs, Is.EqualTo(authoredMap.MonsterSpawns[0].RespawnMs));

        MonsterDefinition monster = content.Monsters[authoredMonster.Id];
        Assert.That(monster.Hp, Is.EqualTo(authoredMonster.Hp));
        Assert.That(monster.Behavior, Is.EqualTo(authoredMonster.Behavior));
        Assert.That(monster.Drops[0].Item, Is.EqualTo(authoredMonster.Drops[0].Item));
        Assert.That(monster.Drops[0].Chance, Is.EqualTo(authoredMonster.Drops[0].Chance));
    }

    [Test]
    public void Load_WhenManifestMissing_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files.Remove(Manifest);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("manifest.json: the package has no manifest"));
    }

    [Test]
    public void Load_WhenManifestIsNotJson_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files[Manifest] = new byte[] { (byte)'{', (byte)'x' };

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("manifest.json: invalid JSON"));
    }

    [Test]
    public void Load_WhenFileDiffersFromManifestHash_FailsWithoutParsingIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.ReplaceWithoutManifest(files, Items, "\"sellPrice\": 2", "\"sellPrice\": 2000000");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(
            problems,
            Is.EqualTo(new[] { "items.json: content does not match the SHA-256 recorded in the manifest" }));
    }

    [Test]
    public void Load_WhenListedFileIsMissing_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files.Remove(Skills);

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "skills.json: listed in the manifest but missing from the package" }));
    }

    [Test]
    public void Load_WhenUnlistedFileIsPresent_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files["extra.json"] = new byte[] { (byte)'{', (byte)'}' };

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { "extra.json: not listed in the manifest" }));
    }

    [Test]
    public void Load_WhenKnownDataFileIsNotListed_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files.Remove(Skills);
        PackageFixture.RewriteManifest(files);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("'skills.json' is not listed"));
    }

    [Test]
    public void Load_WhenManifestListsUnknownDataFile_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        files["quests.json"] = files[Skills];
        PackageFixture.RewriteManifest(files);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("'quests.json' is not a known data file"));
    }

    [Test]
    public void Load_WhenManifestSchemaVersionUnsupported_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.ReplaceWithoutManifest(files, Manifest, "\"schemaVersion\": 1", "\"schemaVersion\": 2");

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("schema version 2 is not supported"));
    }

    [Test]
    public void Load_WhenDataFileSchemaVersionUnsupported_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, Maps, "\"schemaVersion\": 1", "\"schemaVersion\": 2");

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("maps.json: schemaVersion: schema version 2"));
    }

    [Test]
    public void Load_WhenServerVersionDoesNotMatchFiles_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.RepositoryContentDirectory);
        PackageFixture.ReplaceWithoutManifest(files, Manifest, pipeline.Packages!.Server.Version, "00000000deadbeef");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "manifest.json: serverContentVersion: does not match the listed files" }));
    }

    [TestCase("F0EC7E1BB20BFA20")]
    [TestCase("f0ec7e1b")]
    [TestCase("not-a-version-00")]
    public void Load_WhenClientVersionIsMalformed_Fails(string version)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.RepositoryContentDirectory);
        PackageFixture.ReplaceWithoutManifest(files, Manifest, pipeline.Packages!.Client.Version, version);

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "manifest.json: clientContentVersion: must be 16 lowercase hexadecimal digits" }));
    }

    [TestCase(Items, "\"weight\": 1", "\"weight\": 1,\n      \"secret\": 7",
        "items.json: definitions[0].secret: unknown")]
    [TestCase(Items, "      \"weight\": 1,\n", "", "items.json: definitions[0].weight: required property is missing")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": \"999\"", "definitions[0].stackLimit: must be a whole")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": 9.5", "definitions[0].stackLimit: must be a whole")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": 0", "definitions[0].stackLimit: must be at least 1")]
    [TestCase(Items, "\"type\": \"material\"", "\"type\": \"Material\"", "definitions[0].type: unknown value")]
    [TestCase(Jobs, "\"id\": \"job.adventurer\"", "\"id\": \"map.adventurer\"",
        "id: 'map.adventurer' is not a valid ID")]
    [TestCase(Items, "\"weight\": 1", "\"weight\": 1,\n      \"weight\": 2", "weight: appears more than once")]
    [TestCase(Items, "\"definitions\": [", "\"more\": 1,\n  \"definitions\": [", "items.json: more: unknown property")]
    [TestCase(Monsters, "\"chance\": 0.7", "\"chance\": 1.5", "drops[0].chance: must be between 0 and 1")]
    [TestCase(Monsters, "\"minAmount\": 1", "\"minAmount\": 3", "drops[0].minAmount: must not be greater")]
    [TestCase(Monsters, "\"baseSpeed\": 4", "\"baseSpeed\": -4", "definitions[0].baseSpeed: must not be negative")]
    [TestCase(Monsters, "\"behavior\": \"passive\"", "\"behavior\": \"sleepy\"", "behavior: unknown value 'sleepy'")]
    [TestCase(Skills, "\"range\": 1.5", "\"range\": true", "skills.json: definitions[0].range: must be a number")]
    [TestCase(Jobs, "\"agi\": 5", "\"agi\": -1", "jobs.json: definitions[0].startingStats.agi: must be at least 0")]
    [TestCase(Jobs, "\"luk\": 5", "\"luk\": 5,\n        \"cha\": 5", "startingStats.cha: unknown property")]
    [TestCase(Maps, "\"z\": 1\n        }", "\"z\": 0\n        }", "spawnPoint.facing: must not be the zero direction")]
    [TestCase(Maps, "\"count\": 4", "\"count\": 0",
        "maps.json: definitions[0].monsterSpawns[0].count: must be at least")]
    public void Load_WithOneDefectInADataFile_ReportsExactlyThatDefect(
        string file,
        string oldText,
        string newText,
        string expectedProblem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, file, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain(expectedProblem));
    }

    [TestCase(Jobs, "\"startingMap\": \"map.training_ground\"", "\"startingMap\": \"map.nowhere\"", "unknown map")]
    [TestCase(Jobs, "\"basicAttack\": \"skill.basic_attack\"", "\"basicAttack\": \"skill.none\"", "unknown skill")]
    [TestCase(Monsters, "\"item\": \"item.material.slime_gel\"", "\"item\": \"item.nothing\"", "unknown item")]
    [TestCase(Maps, "\"monster\": \"monster.training_slime\"", "\"monster\": \"monster.ghost\"", "unknown monster")]
    public void Load_WhenReferenceIsUnresolved_Fails(string file, string oldText, string newText, string expected)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, file, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.StartWith(file).And.Contain(expected));
    }

    [Test]
    public void Load_WhenDefinitionIsDuplicated_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        string skills = PackageFixture.ReadText(files, Skills);
        int start = skills.IndexOf("    {", StringComparison.Ordinal);
        int end = skills.IndexOf("    }", StringComparison.Ordinal) + "    }".Length;
        string definition = skills.Substring(start, end - start);
        PackageFixture.Replace(files, Skills, definition, definition + ",\n" + definition);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("definitions[1].id: 'skill.basic_attack' is defined more than once"));
    }

    [Test]
    public void Load_WithSeveralDefects_ReportsAllOfThem()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, Items, "\"stackLimit\": 999", "\"stackLimit\": 0");
        PackageFixture.Replace(files, Skills, "\"range\": 1.5", "\"range\": -1");
        PackageFixture.Replace(files, Maps, "\"count\": 4", "\"count\": 0");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(3));
        Assert.That(problems, Has.Exactly(1).StartsWith("items.json"));
        Assert.That(problems, Has.Exactly(1).StartsWith("skills.json"));
        Assert.That(problems, Has.Exactly(1).StartsWith("maps.json"));
    }

    [Test]
    public void Load_WhenReferencedDefinitionIsRejected_DoesNotAlsoReportTheReference()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, Monsters, "\"hp\": 50", "\"hp\": 0");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Is.EqualTo(new[] { "monsters.json: definitions[0].hp: must be at least 1" }));
    }

    [Test]
    public void Load_WhenReferencedDefinitionChangesId_ReportsTheDanglingReference()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.Replace(files, Items, "\"id\": \"item.material.slime_gel\"", "\"id\": \"item.material.goo\"");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("drops unknown item 'item.material.slime_gel'"));
    }

    [Test]
    public void Load_Result_CannotBeMutated()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());
        MonsterDefinition monster = content.Monsters.Values.Single();
        Action addItem = () => ((IDictionary<ItemDefinitionId, ItemDefinition>)content.Items).Clear();
        Action addDrop = () => ((IList<MonsterDrop>)monster.Drops).Clear();

        Assert.That(addItem, Throws.InstanceOf<NotSupportedException>());
        Assert.That(addDrop, Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void LoadFromDirectory_ForWrittenPackage_Loads()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        PackageFixture.WriteTo(root.Path, PackageFixture.BuildRepositoryPackage());

        ServerContent content = ServerContentLoader.LoadFromDirectory(root.Path);

        Assert.That(content.Maps.Keys.Select(id => id.Value), Does.Contain("map.training_ground"));
    }

    [Test]
    public void LoadFromDirectory_WhenDirectoryMissing_Fails()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        string missing = System.IO.Path.Combine(root.Path, "nowhere");
        Action load = () => ServerContentLoader.LoadFromDirectory(missing);

        Assert.That(load, Throws.InstanceOf<ContentLoadException>().With.Message.Contains("does not exist"));
    }

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

        return new string[0];
    }
}
}
