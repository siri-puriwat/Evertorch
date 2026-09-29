using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    private const string Experience = "experience.json";

    [TestCase("\"columns\": 48", "\"columns\": 47", "navigation.cellRows[0]: has 48 symbols but columns is 47")]
    [TestCase("\"rows\": 48", "\"rows\": 47", "navigation.cellRows: has 48 rows but rows is 47")]
    [TestCase("\"columns\": 48", "\"columns\": 4800", "navigation.columns: a grid may have at most 512")]
    [TestCase("\"surface\": \"wall\"", "\"surface\": \"lava\"", "navigation.legend[0].surface: unknown value")]
    [TestCase("\"symbol\": \"b\"", "\"symbol\": \"a\"", "navigation.legend[1].symbol: 'a' appears more than once")]
    [TestCase("\"symbol\": \"b\"", "\"symbol\": \"bb\"", "navigation.legend[1].symbol: must be a single")]
    [TestCase("\"agentRadius\": 0.3", "\"agentRadius\": 0", "definitions[0].navigation: Agent radius must be positive")]
    [TestCase("\"maxStepHeight\": 0.4", "\"maxStepHeight\": 0.01", "definitions[0].navigation: A ramp is too steep")]
    [TestCase("\"originX\": -24", "\"originX\": \"west\"", "navigation.originX: must be a number")]
    [TestCase("\"cellSize\": 1,", "\"cellSize\": 1,\n        \"bake\": true,", "navigation.bake: unknown property")]
    public void Load_WithOneDefectInNavigation_ReportsExactlyThatDefect(
        string oldText,
        string newText,
        string expectedProblem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Maps, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1), string.Join(" | ", problems));
        Assert.That(problems[0], Does.StartWith("maps.json: ").And.Contain(expectedProblem));
    }

    [TestCase("F0EC7E1BB20BFA20")]
    [TestCase("f0ec7e1b")]
    [TestCase("not-a-version-00")]
    public void Load_WhenClientVersionIsMalformed_Fails(string version)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.FixtureContentDirectory);
        PackageFixture.ReplaceWithoutManifest(files, Manifest, pipeline.Packages!.Client.Version, version);

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "manifest.json: clientContentVersion: must be 16 lowercase hexadecimal digits" }));
    }

    [TestCase(
        Items,
        "\"weight\": 1",
        "\"weight\": 1,\n      \"secret\": 7",
        "items.json: definitions[0].secret: unknown")]
    [TestCase(Items, "      \"weight\": 1,\n", "", "items.json: definitions[0].weight: required property is missing")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": \"999\"", "definitions[0].stackLimit: must be a whole")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": 9.5", "definitions[0].stackLimit: must be a whole")]
    [TestCase(Items, "\"stackLimit\": 999", "\"stackLimit\": 0", "definitions[0].stackLimit: must be at least 1")]
    [TestCase(Items, "\"type\": \"material\"", "\"type\": \"Material\"", "definitions[0].type: unknown value")]
    [TestCase(
        Jobs,
        "\"id\": \"job.adventurer\"",
        "\"id\": \"map.adventurer\"",
        "id: 'map.adventurer' is not a valid ID")]
    [TestCase(Items, "\"weight\": 1", "\"weight\": 1,\n      \"weight\": 2", "weight: appears more than once")]
    [TestCase(Items, "\"definitions\": [", "\"more\": 1,\n  \"definitions\": [", "items.json: more: unknown property")]
    [TestCase(Monsters, "\"chance\": 0.7", "\"chance\": 1.5", "drops[0].chance: must be between 0 and 1")]
    [TestCase(Monsters, "\"minAmount\": 1", "\"minAmount\": 3", "drops[0].minAmount: must not be greater")]
    [TestCase(Monsters, "\"baseSpeed\": 4", "\"baseSpeed\": -4", "definitions[0].baseSpeed: must not be negative")]
    [TestCase(Monsters, "\"behavior\": \"passive\"", "\"behavior\": \"sleepy\"", "behavior: unknown value 'sleepy'")]
    [TestCase(Skills, "\"range\": 1.5", "\"range\": true", "skills.json: definitions[0].range: must be a number")]
    [TestCase(Skills, "\"maxLevel\": 0", "\"maxLevel\": -1",
        "skills.json: definitions[0].maxLevel: must be at least 0")]
    [TestCase(Skills, "\"maxLevel\": 0", "\"maxLevel\": 1",
        "skills.json: definitions[0].levels: must list exactly maxLevel (1)")]
    [TestCase(Skills, "\"resolution\"", "\"later\"", "definitions[0].spPaidAt: unknown value 'later'")]
    [TestCase(
        Skills,
        "\"levels\": []",
        "\"levels\": [ { \"spCost\": 0, \"fixedCastMs\": 0, \"variableCastMs\": 0, \"afterCastDelayMs\": 0, "
        + "\"cooldownMs\": 0, \"effect\": { \"damageRatio\": 0 } } ]",
        "definitions[0].levels[0].effect.damageRatio: must be at least 1")]
    [TestCase(
        Skills,
        "\"levels\": []",
        "\"levels\": [ { \"spCost\": 0, \"fixedCastMs\": 0, \"variableCastMs\": 0, \"afterCastDelayMs\": 0, "
        + "\"cooldownMs\": 0, \"effect\": {} } ]",
        "definitions[0].levels[0].effect.damageRatio: an effect must have exactly one of damageRatio, healHp, or status")]
    [TestCase(
        Skills,
        "      \"damageType\": \"physical\",\n",
        "",
        "jobs.json: job.adventurer: its basic attack 'skill.basic_attack' has no damage type")]
    [TestCase(Jobs, "\"skills\": []", "\"skills\": [\"skill.none\"]", "knows unknown skill 'skill.none'")]
    [TestCase(
        Jobs,
        "\"skills\": []",
        "\"skills\": [\"skill.basic_attack\"]",
        "knows skill 'skill.basic_attack', which has no effect")]
    [TestCase(Jobs, "\"skills\": []", "\"skills\": [\"Skill\"]", "'Skill' is not a valid skill ID")]
    [TestCase(
        Experience,
        "[\n        30,",
        "[\n        0,",
        "experience.json: definitions[0].levels[0]: must be at least 1")]
    [TestCase(
        Experience,
        "[\n        30,",
        "[\n        \"30\",",
        "experience.json: definitions[0].levels[0]: must be a whole number")]
    [TestCase(Monsters, "\"baseExperience\": 10", "\"baseExperience\": -1", "baseExperience: must be at least 0")]
    [TestCase(
        Monsters,
        "\"maxAmount\": 2",
        "\"maxAmount\": 1000",
        "monster.training_slime: drops up to 1000 of 'item.material.slime_gel', more than its stack limit of 999")]
    [TestCase(Monsters, "\"keepDistance\": 0", "\"keepDistance\": 1.5", "keepDistance: must be below attackRange")]
    [TestCase(Monsters, "\"magicAttack\": 0", "\"magicAttack\": -1", "magicAttack: must be at least 0")]
    [TestCase(
        Monsters,
        "\"skills\": []",
        "\"skills\": [{ \"skill\": \"skill.basic_attack\", \"chance\": 1.5 }]",
        "skills[0].chance: must be between 0 and 1")]
    [TestCase(Monsters, "\"baseExperience\": 10,\n", "", "definitions[0].baseExperience: required property is")]
    [TestCase(Jobs, "\"agi\": 5", "\"agi\": -1", "jobs.json: definitions[0].startingStats.agi: must be at least 0")]
    [TestCase(Jobs, "\"luk\": 5", "\"luk\": 5,\n        \"cha\": 5", "startingStats.cha: unknown property")]
    [TestCase(Maps, "\"z\": 1\n        }", "\"z\": 0\n        }", "spawnPoint.facing: must not be the zero direction")]
    [TestCase(
        Maps,
        "\"count\": 4",
        "\"count\": 0",
        "maps.json: definitions[0].monsterSpawns[0].count: must be at least")]
    public void Load_WithOneDefectInADataFile_ReportsExactlyThatDefect(
        string file,
        string oldText,
        string newText,
        string expectedProblem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, file, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain(expectedProblem));
    }

    [TestCase(Jobs, "\"startingMap\": \"map.training_ground\"", "\"startingMap\": \"map.nowhere\"", "unknown map")]
    [TestCase(Jobs, "\"basicAttack\": \"skill.basic_attack\"", "\"basicAttack\": \"skill.none\"", "unknown skill")]
    [TestCase(
        Jobs,
        "\"experienceTable\": \"experience.adventurer\"",
        "\"experienceTable\": \"experience.none\"",
        "uses unknown experience table 'experience.none'")]
    [TestCase(Monsters, "\"item\": \"item.material.slime_gel\"", "\"item\": \"item.nothing\"", "unknown item")]
    [TestCase(Maps, "\"monster\": \"monster.training_slime\"", "\"monster\": \"monster.ghost\"", "unknown monster")]
    [TestCase(
        Monsters,
        "\"skills\": []",
        "\"skills\": [{ \"skill\": \"skill.none\", \"chance\": 0.5 }]",
        "casts unknown skill 'skill.none'")]
    [TestCase(
        Monsters,
        "\"skills\": []",
        "\"skills\": [{ \"skill\": \"skill.basic_attack\", \"chance\": 0.5 }]",
        "casts skill 'skill.basic_attack', which has no effect")]
    public void Load_WhenReferenceIsUnresolved_Fails(string file, string oldText, string newText, string expected)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, file, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.StartWith(file).And.Contain(expected));
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

    [TestCase("\"idlePauseMinMs\": 4000", "\"idlePauseMinMs\": 5001", "idlePauseMinMs: must not be greater than")]
    [TestCase("\"scanIntervalMs\": 100", "\"scanIntervalMs\": 0", "scanIntervalMs: must be at least 1")]
    [TestCase("\"roamRadius\": 6", "\"roamRadius\": -6", "roamRadius: must not be negative")]
    [TestCase("\"roamRadius\": 6,", "", "roamRadius")]
    public void Load_WhenAMonsterAiFieldIsMissingOrOutOfRange_Fails(string oldText, string newText, string problem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Monsters, oldText, newText);

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain(problem));
    }

    private const string StatusEffectsFile = "status-effects.json";

    private static string Portal(string center, string map, string position, string facing = "\"x\": 1, \"z\": 0")
    {
        return "\"portals\": [ { \"center\": { " + center + " }, \"radius\": 1, \"destination\": { \"map\": \""
            + map + "\", \"position\": { " + position + " }, \"facing\": { " + facing + " } } } ]";
    }

    private static void AddFocus(Dictionary<string, byte[]> files)
    {
        PackageFixture.Replace(
            files,
            StatusEffectsFile,
            "\"definitions\": []",
            "\"definitions\": [ { \"id\": \"status.focus\", \"displayName\": \"Focus\" } ]");
    }

    // The fixture's basic attack given one level whose effect is <paramref name="effect" />.
    private static void GiveTheBasicAttackALevel(Dictionary<string, byte[]> files, string effect)
    {
        PackageFixture.Replace(files, Skills, "\"maxLevel\": 0", "\"maxLevel\": 1");
        PackageFixture.Replace(
            files,
            Skills,
            "\"levels\": []",
            "\"levels\": [ { \"spCost\": 0, \"fixedCastMs\": 0, \"variableCastMs\": 0, \"afterCastDelayMs\": 0, "
            + $"\"cooldownMs\": 0, \"effect\": {effect} }} ]");
    }

    private static void MakeTheBasicAttackApply(
        Dictionary<string, byte[]> files,
        string status,
        string targetType,
        string agility = "100")
    {
        PackageFixture.Replace(files, Skills, "\"targetType\": \"enemy\"", $"\"targetType\": \"{targetType}\"");
        GiveTheBasicAttackALevel(
            files,
            $"{{ \"status\": \"{status}\", \"durationMs\": 60000, \"statPercent\": {{ \"str\": 0, \"agi\": "
            + $"{agility}, \"vit\": 0, \"int\": 0, \"dex\": 100, \"luk\": 0 }} }}");
    }

    [TestCase("map.elsewhere", "\"x\": 2, \"y\": 0, \"z\": 0", "a portal leads to unknown map 'map.elsewhere'")]
    [TestCase("map.training_ground", "\"x\": -999, \"y\": 0, \"z\": 0",
        "arrival on 'map.training_ground' is not a place")]
    [TestCase("map.training_ground", "\"x\": 3, \"y\": 0, \"z\": -3",
        "arrival lies inside a portal of 'map.training_ground'")]
    public void Load_WhenAPortalLeadsNowhereToStand_Fails(string map, string position, string problem)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Maps, "\"portals\": []",
            Portal("\"x\": 3.5, \"y\": 0, \"z\": -3.5", map, position));

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.StartWith("maps.json: map.training_ground: ").And.Contain(problem));
    }

    private static bool GateBeside(MapDefinition map, WorldPosition portal, float step)
    {
        return map.Navigation.TryGetCellIndex(portal.X + step, portal.Z, out int column, out int row)
            && map.Navigation.GetCell(column, row).Surface == NavigationSurface.Gate;
    }

    [Test]
    public void LoadFromDirectory_ForWrittenPackage_Loads()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(root.Path, PackageFixture.BuildFixturePackage());

        ServerContent content = ServerContentLoader.LoadFromDirectory(root.Path);

        Assert.That(content.Maps.Keys.Select(id => id.Value), Does.Contain("map.training_ground"));
    }

    [Test]
    public void LoadFromDirectory_WhenDirectoryMissing_Fails()
    {
        using var root = new TemporaryDirectory();
        string missing = Path.Combine(root.Path, "nowhere");
        Action load = () => ServerContentLoader.LoadFromDirectory(missing);

        Assert.That(load, Throws.InstanceOf<ContentLoadException>().With.Message.Contains("does not exist"));
    }

    [Test]
    public void Load_ForFixtureContent_ReadsTheSameValuesTheToolsParsed()
    {
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.FixtureContentDirectory);
        JobDefinition authoredJob = pipeline.Content.Jobs.Single().Definition;
        MapDefinition authoredMap = pipeline.Content.Maps.Single().Definition;
        MonsterDefinition authoredMonster = pipeline.Content.Monsters.Single().Definition;
        ExperienceTableDefinition authoredTable = pipeline.Content.ExperienceTables.Single().Definition;

        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildFixturePackage());

        JobDefinition job = content.Jobs[authoredJob.Id];
        Assert.That(job.StartingStats, Is.EqualTo(authoredJob.StartingStats));
        Assert.That(job.BaseSpeed, Is.EqualTo(authoredJob.BaseSpeed));
        Assert.That(job.StartingMap, Is.EqualTo(authoredJob.StartingMap));
        Assert.That(job.BasicAttack, Is.EqualTo(authoredJob.BasicAttack));
        Assert.That(job.UnarmedAttackSpeedPenalty, Is.EqualTo(authoredJob.UnarmedAttackSpeedPenalty));
        Assert.That(job.ExperienceTable, Is.EqualTo(authoredJob.ExperienceTable));

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
        Assert.That(monster.BaseExperience, Is.EqualTo(authoredMonster.BaseExperience));

        Assert.That(content.ExperienceTables[authoredTable.Id].Levels, Is.EqualTo(authoredTable.Levels));
    }

    [Test]
    public void Load_ForFixtureContent_RebuildsTheSameNavigationGridTheToolsAuthored()
    {
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.FixtureContentDirectory);
        MapDefinition authoredMap = pipeline.Content.Maps.Single().Definition;
        NavigationGrid authored = authoredMap.Navigation;

        NavigationGrid loaded = ServerContentLoader.Load(PackageFixture.BuildFixturePackage())
            .Maps[authoredMap.Id]
            .Navigation;

        Assert.That(loaded.Columns, Is.EqualTo(authored.Columns));
        Assert.That(loaded.Rows, Is.EqualTo(authored.Rows));
        Assert.That(loaded.CellSize, Is.EqualTo(authored.CellSize));
        Assert.That(loaded.OriginX, Is.EqualTo(authored.OriginX));
        Assert.That(loaded.OriginZ, Is.EqualTo(authored.OriginZ));
        Assert.That(loaded.AgentRadius, Is.EqualTo(authored.AgentRadius));
        Assert.That(loaded.MaxStepHeight, Is.EqualTo(authored.MaxStepHeight));
        for (int row = 0; row < authored.Rows; row++)
        {
            for (int column = 0; column < authored.Columns; column++)
            {
                Assert.That(loaded.GetCell(column, row), Is.EqualTo(authored.GetCell(column, row)));
            }
        }
    }

    [Test]
    public void Load_ForRepositoryContent_JoinsTheGroundAndTheFieldBothWays()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());
        MapDefinition ground = content.Maps[new MapDefinitionId("map.training_ground")];
        MapDefinition field = content.Maps[new MapDefinitionId("map.training_field")];

        MapPortal toField = ground.Portals.Single();
        MapPortal toGround = field.Portals.Single();

        Assert.That(toField.DestinationMap, Is.EqualTo(field.Id));
        Assert.That(toGround.DestinationMap, Is.EqualTo(ground.Id));
        Assert.That(toField.DestinationPosition, Is.EqualTo(field.SpawnPosition), "arrivals by the field's gate");
        Assert.That(field.IsInPortal(toField.DestinationPosition), Is.False);
        Assert.That(ground.IsInPortal(toGround.DestinationPosition), Is.False);
        Assert.That(ground.IsInPortal(ground.SpawnPosition) || field.IsInPortal(field.SpawnPosition), Is.False);
        Assert.That((toField.DestinationFacing.X, toGround.DestinationFacing.X), Is.EqualTo((1f, -1f)));
        Assert.That(GateBeside(ground, toField.Center, 1f), Is.True, "the ground's portal lies before its east gate");
        Assert.That(GateBeside(field, toGround.Center, -1f), Is.True, "the field's lies before its west gate");
    }

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
        Assert.That(
            content.ExperienceTables.Keys.Select(id => id.Value),
            Is.EquivalentTo(pipeline.Content.ExperienceTables.Select(table => table.Definition.Id.Value)));
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsEachConsumablesEffect()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        ItemDefinition health = content.Items[new ItemDefinitionId("item.consumable.minor_health")];
        ItemDefinition mana = content.Items[new ItemDefinitionId("item.consumable.minor_mana")];
        ItemDefinition gel = content.Items[new ItemDefinitionId("item.material.slime_gel")];

        Assert.That((health.StackLimit, health.Effect!.Health, health.Effect.Spirit), Is.EqualTo((50, 30, 0)));
        Assert.That((mana.StackLimit, mana.Effect!.Health, mana.Effect.Spirit), Is.EqualTo((50, 0, 15)));
        Assert.That(gel.Effect, Is.Null);
    }

    [Test]
    public void Load_ForRepositoryContent_ReadsEachItemsEquipmentAndSlot()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

        ItemDefinition sword = content.Items[new ItemDefinitionId("item.weapon.training_sword")];
        ItemDefinition staff = content.Items[new ItemDefinitionId("item.weapon.training_staff")];
        ItemDefinition armor = content.Items[new ItemDefinitionId("item.armor.cloth")];
        ItemDefinition gel = content.Items[new ItemDefinitionId("item.material.slime_gel")];

        Assert.That(
            (sword.Slot, sword.StackLimit, sword.Equipment!.Attack, sword.Equipment.AttackSpeedPenalty),
            Is.EqualTo((EquipmentSlot.Weapon, 1, 20, 50)));
        Assert.That((staff.Equipment!.Attack, staff.Equipment.Bonus.Int), Is.EqualTo((8, 10)));
        Assert.That((armor.Slot, armor.Equipment!.Defense, armor.Equipment.Attack),
            Is.EqualTo((EquipmentSlot.Armor, 8, 0)));
        Assert.That((gel.Slot, gel.Equipment), Is.EqualTo((EquipmentSlot.None, (ItemEquipment?)null)));
    }

    [Test]
    public void Load_Result_CannotBeMutated()
    {
        ServerContent content = ServerContentLoader.Load(PackageFixture.BuildFixturePackage());
        MonsterDefinition monster = content.Monsters.Values.Single();
        Action addItem = () => ((IDictionary<ItemDefinitionId, ItemDefinition>)content.Items).Clear();
        Action addDrop = () => ((IList<MonsterDrop>)monster.Drops).Clear();

        Assert.That(addItem, Throws.InstanceOf<NotSupportedException>());
        Assert.That(addDrop, Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void Load_WhenAJobKnowsMoreSkillsThanASkillListCarries_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string twelve = string.Join(", ", Enumerable.Range(1, 12).Select(index => $"\"skill.s{index}\""));
        PackageFixture.Replace(files, Jobs, "\"skills\": []", $"\"skills\": [{twelve}]");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(
            problems,
            Is.EqualTo(new[]
                { "jobs.json: definitions[0].skills: lists more than the 11 skills a skill list carries" }));
    }

    [Test]
    public void Load_WhenASelfSkillDealsDamage_OrAMonsterCastsASkillMeantForItsCaster_Fails()
    {
        Dictionary<string, byte[]> selfDamage = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(selfDamage, Skills, "\"targetType\": \"enemy\"", "\"targetType\": \"self\"");
        GiveTheBasicAttackALevel(selfDamage, "{ \"damageRatio\": 100 }");
        Dictionary<string, byte[]> castForItself = PackageFixture.BuildFixturePackage();
        AddFocus(castForItself);
        MakeTheBasicAttackApply(castForItself, "status.focus", "self");
        PackageFixture.Replace(
            castForItself,
            Monsters,
            "\"skills\": []",
            "\"skills\": [{ \"skill\": \"skill.basic_attack\", \"chance\": 0.5 }]");

        Assert.That(
            ProblemsOf(selfDamage),
            Is.EqualTo(new[] { "skills.json: definitions[0].targetType: must be enemy for a damage effect" }));
        Assert.That(
            ProblemsOf(castForItself),
            Is.EqualTo(
                new[]
                {
                    "monsters.json: monster.training_slime: casts skill 'skill.basic_attack', which is not cast at an "
                    + "enemy"
                }));
    }

    [Test]
    public void Load_WhenASkillAppliesAnUnknownStatusEffect_OrAppliesOneToAnEnemy_Fails()
    {
        Dictionary<string, byte[]> unknown = PackageFixture.BuildFixturePackage();
        MakeTheBasicAttackApply(unknown, "status.none", "self");
        Dictionary<string, byte[]> enemy = PackageFixture.BuildFixturePackage();
        AddFocus(enemy);
        MakeTheBasicAttackApply(enemy, "status.focus", "enemy");

        Assert.That(
            ProblemsOf(unknown),
            Is.EqualTo(new[] { "skills.json: skill.basic_attack: applies unknown status effect 'status.none'" }));
        Assert.That(
            ProblemsOf(enemy),
            Is.EqualTo(new[] { "skills.json: definitions[0].targetType: must be self for a status effect" }));
    }

    [Test]
    public void Load_WhenASkillLevelsStatusAddsMoreThanATenfold_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        AddFocus(files);
        MakeTheBasicAttackApply(files, "status.focus", "self", "1001");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "skills.json: definitions[0].levels[0].effect.statPercent.agi: must be at most 1000" }));
    }

    [Test]
    public void Load_WhenAStatusEffectStillNamesItsStrength_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(
            files,
            StatusEffectsFile,
            "\"definitions\": []",
            "\"definitions\": [ { \"id\": \"status.focus\", \"displayName\": \"Focus\", \"statPercent\": {} } ]");

        Assert.That(ProblemsOf(files), Has.Some.Contains("statPercent"));
    }

    [Test]
    public void Load_WhenAnItemsEffectDoesNotMatchItsType_Fails()
    {
        Dictionary<string, byte[]> material = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(material, Items, "\"weight\": 1",
            "\"weight\": 1,\n      \"effect\": { \"hp\": 1, \"sp\": 0 }");
        Dictionary<string, byte[]> consumable = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(consumable, Items, "\"type\": \"material\"", "\"type\": \"consumable\"");
        Dictionary<string, byte[]> empty = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(empty, Items, "\"type\": \"material\"", "\"type\": \"consumable\"");
        PackageFixture.Replace(empty, Items, "\"weight\": 1",
            "\"weight\": 1,\n      \"effect\": { \"hp\": 0, \"sp\": 0 }");

        Assert.That(ProblemsOf(material), Has.Some.Contains("effect: is only for a consumable"));
        Assert.That(ProblemsOf(consumable), Has.Some.Contains("effect: required property is missing"));
        Assert.That(ProblemsOf(empty), Has.Some.Contains("effect: must restore HP, SP, or both"));
    }

    [Test]
    public void Load_WhenAnItemsEquipmentDoesNotMatchItsType_Fails()
    {
        Dictionary<string, byte[]> material = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(
            material,
            Items,
            "\"weight\": 1",
            "\"weight\": 1,\n      \"equipment\": { \"attack\": 1, \"attackSpeedPenalty\": 1, \"defense\": 0, "
            + "\"bonus\": { \"str\": 0, \"agi\": 0, \"vit\": 0, \"int\": 0, \"dex\": 0, \"luk\": 0 } }");
        Dictionary<string, byte[]> weapon = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(weapon, Items, "\"type\": \"material\"", "\"type\": \"weapon\"");

        Assert.That(ProblemsOf(material), Has.Some.Contains("equipment: is only for a weapon or armor"));
        Assert.That(ProblemsOf(weapon), Has.Some.Contains("stackLimit: must be 1 for a weapon or armor"));
        Assert.That(ProblemsOf(weapon), Has.Some.Contains("equipment: required property is missing"));
    }

    [Test]
    public void Load_WhenDataFileSchemaVersionUnsupported_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Maps, "\"schemaVersion\": 1", "\"schemaVersion\": 2");

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("maps.json: schemaVersion: schema version 2"));
    }

    [Test]
    public void Load_WhenDefinitionIdIsLongerThanTheWireLimit_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string tooLong = $"job.{new string('a', 61)}";
        PackageFixture.Replace(files, Jobs, "\"id\": \"job.adventurer\"", $"\"id\": \"{tooLong}\"");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("jobs.json: definitions[0].id").And.Contain("is not a valid ID"));
    }

    [Test]
    public void Load_WhenDefinitionIsDuplicated_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string skills = PackageFixture.ReadText(files, Skills);
        int start = skills.IndexOf("    {", StringComparison.Ordinal);
        int end = skills.IndexOf("    }", StringComparison.Ordinal) + "    }".Length;
        string definition = skills.Substring(start, end - start);
        PackageFixture.Replace(files, Skills, definition, $"{definition},\n{definition}");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("definitions[1].id: 'skill.basic_attack' is defined more than once"));
    }

    // Every level naming the same unknown status is one problem, not one a level (M9 review).
    [Test]
    public void Load_WhenEveryLevelOfASkillAppliesTheSameUnknownStatusEffect_SaysSoOnce()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string level = "{ \"spCost\": 0, \"fixedCastMs\": 0, \"variableCastMs\": 0, \"afterCastDelayMs\": 0, "
            + "\"cooldownMs\": 0, \"effect\": { \"status\": \"status.none\", \"durationMs\": 60000, "
            + "\"statPercent\": { \"str\": 0, \"agi\": 100, \"vit\": 0, \"int\": 0, \"dex\": 100, \"luk\": 0 } } }";
        PackageFixture.Replace(files, Skills, "\"targetType\": \"enemy\"", "\"targetType\": \"self\"");
        PackageFixture.Replace(files, Skills, "\"maxLevel\": 0", "\"maxLevel\": 2");
        PackageFixture.Replace(files, Skills, "\"levels\": []", $"\"levels\": [ {level}, {level} ]");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "skills.json: skill.basic_attack: applies unknown status effect 'status.none'" }));
    }

    [Test]
    public void Load_WhenFileDiffersFromManifestHash_FailsWithoutParsingIt()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.ReplaceWithoutManifest(files, Items, "\"sellPrice\": 2", "\"sellPrice\": 2000000");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(
            problems,
            Is.EqualTo(new[] { "items.json: content does not match the SHA-256 recorded in the manifest" }));
    }

    [Test]
    public void Load_WhenKnownDataFileIsNotListed_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files.Remove(Skills);
        PackageFixture.RewriteManifest(files);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("'skills.json' is not listed"));
    }

    [Test]
    public void Load_WhenListedFileIsMissing_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files.Remove(Skills);

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "skills.json: listed in the manifest but missing from the package" }));
    }

    [Test]
    public void Load_WhenManifestIsNotJson_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files[Manifest] = new[] { (byte)'{', (byte)'x' };

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("manifest.json: invalid JSON"));
    }

    [Test]
    public void Load_WhenManifestListsUnknownDataFile_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files["notes.json"] = files[Skills];
        PackageFixture.RewriteManifest(files);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("'notes.json' is not a known data file"));
    }

    [Test]
    public void Load_WhenManifestMissing_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files.Remove(Manifest);

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("manifest.json: the package has no manifest"));
    }

    [Test]
    public void Load_WhenManifestSchemaVersionUnsupported_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.ReplaceWithoutManifest(files, Manifest, "\"schemaVersion\": 1", "\"schemaVersion\": 2");

        Assert.That(ProblemsOf(files), Has.Exactly(1).Contains("schema version 2 is not supported"));
    }

    [Test]
    public void Load_WhenNavigationIsAbsent_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Maps, "\"navigation\": {", "\"geometry\": {");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Exactly(1).Contains("definitions[0].navigation: required property is missing"));
        Assert.That(problems, Has.Exactly(1).Contains("definitions[0].geometry: unknown property"));
        Assert.That(problems, Has.Count.EqualTo(2));
    }

    [Test]
    public void Load_WhenReferencedDefinitionChangesId_ReportsTheDanglingReference()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Items, "\"id\": \"item.material.slime_gel\"", "\"id\": \"item.material.goo\"");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(
            problems,
            Is.EquivalentTo(
                new[]
                {
                    "monsters.json: monster.training_slime: drops unknown item 'item.material.slime_gel'",
                    "npcs.json: npc.quartermaster: sells unknown item 'item.material.slime_gel'"
                }));
    }

    [Test]
    public void Load_WhenReferencedDefinitionIsRejected_DoesNotAlsoReportTheReference()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Monsters, "\"hp\": 50", "\"hp\": 0");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Is.EqualTo(new[] { "monsters.json: definitions[0].hp: must be at least 1" }));
    }

    [Test]
    public void Load_WhenServerVersionDoesNotMatchFiles_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        ContentPipelineResult pipeline = ContentPipeline.Run(PackageFixture.FixtureContentDirectory);
        PackageFixture.ReplaceWithoutManifest(files, Manifest, pipeline.Packages!.Server.Version, "00000000deadbeef");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "manifest.json: serverContentVersion: does not match the listed files" }));
    }

    [Test]
    public void Load_WhenSpawnPointCannotBeStoodOn_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(
            files,
            Maps,
            "\"position\": {\n          \"x\": 0,",
            "\"position\": {\n          \"x\": -23.9,");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[]
            {
                "maps.json: definitions[0].spawnPoint.position: is not a place the navigation grid lets an agent stand"
            }));
    }

    [Test]
    public void Load_WhenTheSpawnPointIsInsideAPortal_OrAPortalHasNoRadius_Fails()
    {
        MapDefinition map = ServerContentLoader.Load(PackageFixture.BuildFixturePackage()).Maps.Values.Single();
        string spawn = string.Format(
            CultureInfo.InvariantCulture,
            "\"x\": {0}, \"y\": {1}, \"z\": {2}",
            map.SpawnPosition.X,
            map.SpawnPosition.Y,
            map.SpawnPosition.Z);
        Dictionary<string, byte[]> onSpawn = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(onSpawn, Maps, "\"portals\": []",
            Portal(spawn, "map.training_ground", "\"x\": 3.5, \"y\": 0, \"z\": -3.5"));
        Dictionary<string, byte[]> noRadius = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(noRadius, Maps, "\"portals\": []",
            Portal(spawn, "map.training_ground", spawn).Replace("\"radius\": 1", "\"radius\": 0"));

        Assert.That(ProblemsOf(onSpawn),
            Is.EqualTo(new[] { "maps.json: definitions[0].spawnPoint.position: lies inside a portal" }));
        Assert.That(ProblemsOf(noRadius),
            Is.EqualTo(new[] { "maps.json: definitions[0].portals[0].radius: must be greater than 0" }));
    }

    [Test]
    public void Load_WhenUnlistedFileIsPresent_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        files["extra.json"] = new[] { (byte)'{', (byte)'}' };

        Assert.That(ProblemsOf(files), Is.EqualTo(new[] { "extra.json: not listed in the manifest" }));
    }

    [Test]
    public void Load_WithMoreStatusEffectsThanAStatusListCarries_Fails()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        string fifteen = string.Join(
            ", ",
            Enumerable.Range(1, 15).Select(index =>
                $"{{ \"id\": \"status.s{index}\", \"displayName\": \"S\" }}"));
        PackageFixture.Replace(files, StatusEffectsFile, "\"definitions\": []", $"\"definitions\": [ {fifteen} ]");

        Assert.That(
            ProblemsOf(files),
            Is.EqualTo(new[] { "status-effects.json: lists more than the 14 status effects a status list carries" }));
    }

    [Test]
    public void Load_WithSeveralDefects_ReportsAllOfThem()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildFixturePackage();
        PackageFixture.Replace(files, Items, "\"stackLimit\": 999", "\"stackLimit\": 0");
        PackageFixture.Replace(files, Skills, "\"range\": 1.5", "\"range\": -1");
        PackageFixture.Replace(files, Maps, "\"count\": 4", "\"count\": 0");

        IReadOnlyList<string> problems = ProblemsOf(files);

        Assert.That(problems, Has.Count.EqualTo(3));
        Assert.That(problems, Has.Exactly(1).StartsWith("items.json"));
        Assert.That(problems, Has.Exactly(1).StartsWith("skills.json"));
        Assert.That(problems, Has.Exactly(1).StartsWith("maps.json"));
    }
}
}
