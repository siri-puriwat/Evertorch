using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientContentParserTests
{
    private const string Maps =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"map.training_ground\",\"displayName\":\"Training Ground\","
        + "\"scene\":\"map_training_ground\",\"navigation\":{\"cellSize\":1,\"originX\":-1,\"originZ\":-2,"
        + "\"agentRadius\":0.3,\"maxStepHeight\":0.4,\"columns\":3,\"rows\":2,\"legend\":["
        + "{\"symbol\":\"a\",\"surface\":\"wall\",\"axis\":\"none\",\"heightAtMin\":0,\"heightAtMax\":0},"
        + "{\"symbol\":\"b\",\"surface\":\"floor\",\"axis\":\"none\",\"heightAtMin\":0,\"heightAtMax\":0},"
        + "{\"symbol\":\"c\",\"surface\":\"floor\",\"axis\":\"x\",\"heightAtMin\":0,\"heightAtMax\":0.25}],"
        + "\"cellRows\":[\"abc\",\"bba\"]}}]}";

    private const string Jobs =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"job.adventurer\",\"displayName\":\"Adventurer\","
        + "\"prefab\":\"character_adventurer\"}]}";

    private const string Monsters =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"monster.training_slime\",\"displayName\":\"Training Slime\","
        + "\"level\":1,\"prefab\":\"monster_training_slime\",\"icon\":\"monster_training_slime_icon\"}]}";

    private const string Items =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"item.material.slime_gel\",\"displayName\":\"Slime Gel\","
        + "\"type\":\"material\",\"stackLimit\":999,\"icon\":\"item_slime_gel\",\"model\":\"pickup_slime_gel\"}]}";

    private const string StatusEffectsJson =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"status.focus\",\"displayName\":\"Focus\","
        + "\"icon\":\"status_focus\"},{\"id\":\"status.plain\",\"displayName\":\"Plain\"}]}";

    private const string Skills =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"skill.first_aid\",\"displayName\":\"First Aid\","
        + "\"targetType\":\"self\",\"icon\":\"skill_first_aid\"},{\"id\":\"skill.strike\","
        + "\"displayName\":\"Strike\",\"targetType\":\"enemy\",\"icon\":\"skill_strike\",\"description\":\"Hits hard.\","
        + "\"projectile\":\"projectile_strike\"}]}";

    private const string Npcs =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"npc.gate_warden\",\"displayName\":\"Gate Warden\","
        + "\"prefab\":\"npc_gate_warden\"},{\"id\":\"npc.quartermaster\",\"displayName\":\"Quartermaster\","
        + "\"prefab\":\"npc_quartermaster\"}]}";

    private const string Quests =
        "{\"schemaVersion\":1,\"definitions\":[{\"id\":\"quest.crawler_hunt\",\"displayName\":\"Crawler Hunt\"}]}";

    [TestCase(ClientContentParser.MapsFile)]
    [TestCase(ClientContentParser.JobsFile)]
    [TestCase(ClientContentParser.MonstersFile)]
    [TestCase(ClientContentParser.ItemsFile)]
    [TestCase(ClientContentParser.SkillsFile)]
    [TestCase(ClientContentParser.StatusEffectsFile)]
    [TestCase(ClientContentParser.NpcsFile)]
    [TestCase(ClientContentParser.QuestsFile)]
    public void Parse_WhenARequiredFileIsNotInThePackage_IsRefused(string fileName)
    {
        var package = Package.Without(fileName);

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Is.EqualTo($"The content package has no '{fileName}'."));
    }

    [TestCase("not json at all")]
    [TestCase("{\"schemaVersion\":1,\"clientContentVersion\":\"x\",\"files\":[]}")]
    public void ReadFileList_ForAnUnreadableOrEmptyManifest_IsEmptyWithAnError(string manifest)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(manifest);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(bytes, out string error);

        Assert.That(names, Is.Empty);
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase(2, "maps.json")]
    [TestCase(1, "../maps.json")]
    [TestCase(1, "sub/maps.json")]
    [TestCase(1, "C:maps.json")]
    [TestCase(1, "manifest.json")]
    [TestCase(1, "")]
    public void ReadFileList_ForAnUnsupportedSchemaOrUnsafeFileName_IsEmptyWithAnError(int schema, string fileName)
    {
        string manifest = $"{{\"schemaVersion\":{schema},\"clientContentVersion\":\"x\","
            + $"\"files\":[{{\"path\":\"{fileName}\",\"sha256\":\"\"}}]}}";

        byte[] bytes = Encoding.UTF8.GetBytes(manifest);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(bytes, out string error);

        Assert.That(names, Is.Empty);
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase("\"surface\":\"wall\"", "\"surface\":\"lava\"", "legend")]
    [TestCase("\"axis\":\"x\"", "\"axis\":\"y\"", "legend")]
    [TestCase("\"symbol\":\"b\"", "\"symbol\":\"a\"", "legend")]
    [TestCase("\"abc\"", "\"abz\"", "outside the legend")]
    [TestCase("\"abc\"", "\"ab\"", "wrong length")]
    [TestCase("\"rows\":2", "\"rows\":3", "do not match")]
    [TestCase("\"heightAtMax\":0.25", "\"heightAtMax\":5", "steep")]
    [TestCase("\"id\":\"map.training_ground\"", "\"id\":\"item.potion\"", "map ID")]
    [TestCase("\"schemaVersion\":1,\"definitions\"", "\"schemaVersion\":9,\"definitions\"", "schema version")]
    public void Parse_ForMalformedMaps_IsRefusedWithAReason(string oldText, string newText, string expected)
    {
        Assert.That(Maps, Does.Contain(oldText));
        var package = new Package(Maps.Replace(oldText, newText));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain(expected));
    }

    [TestCase(
        ClientContentParser.JobsFile,
        "\"id\":\"job.adventurer\"",
        "\"id\":\"monster.adventurer\"",
        "invalid or repeated job ID")]
    [TestCase(
        ClientContentParser.JobsFile,
        "}]}",
        "},{\"id\":\"job.adventurer\",\"displayName\":\"Again\",\"prefab\":\"other\"}]}",
        "invalid or repeated job ID")]
    [TestCase(
        ClientContentParser.JobsFile,
        "\"prefab\":\"character_adventurer\"",
        "\"prefab\":\"\"",
        "Job 'job.adventurer': prefab is not a logical key")]
    [TestCase(
        ClientContentParser.JobsFile,
        ",\"prefab\":\"character_adventurer\"",
        "",
        "Job 'job.adventurer': prefab is not a logical key")]
    [TestCase(
        ClientContentParser.JobsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'jobs.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.MonstersFile,
        "\"id\":\"monster.training_slime\"",
        "\"id\":\"item.training_slime\"",
        "invalid or repeated monster ID")]
    [TestCase(
        ClientContentParser.MonstersFile,
        "\"prefab\":\"monster_training_slime\"",
        "\"prefab\":\"prefabs/monster_training_slime\"",
        "Monster 'monster.training_slime': prefab is not a logical key")]
    [TestCase(
        ClientContentParser.MonstersFile,
        "\"icon\":\"monster_training_slime_icon\"",
        "\"icon\":\"slime icon\"",
        "Monster 'monster.training_slime': icon is not a logical key")]
    [TestCase(
        ClientContentParser.MonstersFile,
        "\"icon\":\"monster_training_slime_icon\"",
        "\"icon\":\"monster_training_slime_icon\",\"projectile\":\"Projectiles/Spark.prefab\"",
        "Monster 'monster.training_slime': projectile is not a logical key")]
    [TestCase(
        ClientContentParser.MonstersFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'monsters.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.ItemsFile,
        "\"id\":\"item.material.slime_gel\"",
        "\"id\":\"job.slime_gel\"",
        "invalid or repeated item ID")]
    [TestCase(
        ClientContentParser.ItemsFile,
        "\"model\":\"pickup_slime_gel\"",
        "\"model\":\"Pickup_Slime_Gel\"",
        "Item 'item.material.slime_gel': model is not a logical key")]
    [TestCase(
        ClientContentParser.ItemsFile,
        "\"icon\":\"item_slime_gel\"",
        "\"icon\":\"Assets/item_slime_gel.png\"",
        "Item 'item.material.slime_gel': icon is not a logical key")]
    [TestCase(
        ClientContentParser.ItemsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'items.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.ItemsFile,
        "\"type\":\"material\"",
        "\"type\":\"shield\"",
        "Item 'item.material.slime_gel': type is not material, consumable, weapon, or armor")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"targetType\":\"self\"",
        "\"targetType\":\"friend\"",
        "Skill 'skill.first_aid': targetType is not enemy, self, or ally")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"id\":\"skill.strike\"",
        "\"id\":\"skill.first_aid\"",
        "'skills.json' has an invalid or repeated skill ID")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"id\":\"skill.strike\"",
        "\"id\":\"item.strike\"",
        "'skills.json' has an invalid or repeated skill ID")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"icon\":\"skill_strike\"",
        "\"icon\":\"Skills/Strike.png\"",
        "Skill 'skill.strike': icon is not a logical key")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"projectile\":\"projectile_strike\"",
        "\"projectile\":\"Bolts/Strike.prefab\"",
        "Skill 'skill.strike': projectile is not a logical key")]
    [TestCase(
        ClientContentParser.SkillsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'skills.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.StatusEffectsFile,
        "\"id\":\"status.plain\"",
        "\"id\":\"skill.plain\"",
        "'status-effects.json' has an invalid or repeated status effect ID")]
    [TestCase(
        ClientContentParser.StatusEffectsFile,
        "\"icon\":\"status_focus\"",
        "\"icon\":\"Status/Focus.png\"",
        "Status effect 'status.focus': icon is not a logical key")]
    [TestCase(
        ClientContentParser.StatusEffectsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'status-effects.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.NpcsFile,
        "\"id\":\"npc.gate_warden\"",
        "\"id\":\"npc.quartermaster\"",
        "'npcs.json' has an invalid or repeated NPC ID")]
    [TestCase(
        ClientContentParser.NpcsFile,
        "\"id\":\"npc.gate_warden\"",
        "\"id\":\"quest.gate_warden\"",
        "'npcs.json' has an invalid or repeated NPC ID")]
    [TestCase(
        ClientContentParser.NpcsFile,
        "\"prefab\":\"npc_gate_warden\"",
        "\"prefab\":\"Npcs/GateWarden.prefab\"",
        "NPC 'npc.gate_warden': prefab is not a logical key")]
    [TestCase(
        ClientContentParser.NpcsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'npcs.json' is not readable or has an unsupported schema version")]
    [TestCase(
        ClientContentParser.QuestsFile,
        "\"id\":\"quest.crawler_hunt\"",
        "\"id\":\"npc.crawler_hunt\"",
        "'quests.json' has an invalid or repeated quest ID")]
    [TestCase(
        ClientContentParser.QuestsFile,
        "\"schemaVersion\":1",
        "\"schemaVersion\":2",
        "'quests.json' is not readable or has an unsupported schema version")]
    public void Parse_ForMalformedDefinitions_IsRefusedWithAReason(
        string fileName,
        string oldText,
        string newText,
        string expected)
    {
        string original = Package.DefaultTexts(Maps)[fileName];
        Assert.That(original, Does.Contain(oldText));
        var package = Package.With(fileName, original.Replace(oldText, newText));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain(expected));
    }

    private sealed class Package
    {
        public Package(string maps, string? versionOverride = null)
            : this(DefaultTexts(maps), versionOverride)
        {
        }

        private Package(IReadOnlyDictionary<string, string> texts, string? versionOverride = null)
        {
            Files = texts.ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetBytes(pair.Value));

            var listing = new StringBuilder();
            var entries = new StringBuilder();
            foreach (string name in Files.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                string hash = Hash(Files[name]);
                listing.Append(name).Append(':').Append(hash).Append('\n');
                entries.Append(entries.Length == 0 ? string.Empty : ",");
                entries.Append("{\"path\":\"").Append(name).Append("\",\"sha256\":\"").Append(hash).Append("\"}");
            }

            Version = versionOverride ?? Hash(Encoding.UTF8.GetBytes(listing.ToString())).Substring(0, 16);
            Manifest = Encoding.UTF8.GetBytes(
                $"{{\"schemaVersion\":1,\"clientContentVersion\":\"{Version}\",\"files\":[{entries}]}}");
        }

        public Dictionary<string, byte[]> Files { get; }

        public byte[] Manifest { get; }

        public string Version { get; }

        public static Dictionary<string, string> DefaultTexts(string maps)
        {
            return new Dictionary<string, string>
            {
                [ClientContentParser.MapsFile] = maps,
                [ClientContentParser.JobsFile] = Jobs,
                [ClientContentParser.MonstersFile] = Monsters,
                [ClientContentParser.ItemsFile] = Items,
                [ClientContentParser.SkillsFile] = Skills,
                [ClientContentParser.StatusEffectsFile] = StatusEffectsJson,
                [ClientContentParser.NpcsFile] = Npcs,
                [ClientContentParser.QuestsFile] = Quests
            };
        }

        public static Package With(string fileName, string text)
        {
            Dictionary<string, string> texts = DefaultTexts(Maps);
            texts[fileName] = text;
            return new Package(texts);
        }

        public static Package Without(string fileName)
        {
            Dictionary<string, string> texts = DefaultTexts(Maps);
            texts.Remove(fileName);
            return new Package(texts);
        }

        private static string Hash(byte[] content)
        {
            using (var sha256 = SHA256.Create())
            {
                return string.Concat(sha256.ComputeHash(content).Select(value => value.ToString("x2")));
            }
        }
    }

    // A heal may name another player (Gameplay Systems §9).
    [Test]
    public void Parse_ForASkillOnAnAlly_ReadsItsTargetType()
    {
        string original = Package.DefaultTexts(Maps)[ClientContentParser.SkillsFile];
        var package = Package.With(
            ClientContentParser.SkillsFile,
            original.Replace("\"targetType\":\"self\"", "\"targetType\":\"ally\""));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Not.Null, error);
        Assert.That(content!.TryGetSkill(new SkillDefinitionId("skill.first_aid"), out ClientSkill? aid), Is.True);
        Assert.That(aid!.TargetType, Is.EqualTo(SkillTargetType.Ally));
    }

    [Test]
    public void Parse_TheGeneratedPackageInStreamingAssets_YieldsTheTrainingGround()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, StreamingContentLoader.FolderName);
        string manifestPath = Path.Combine(folder, ClientContentParser.ManifestFile);
        if (!File.Exists(manifestPath))
        {
            Assert.Ignore($"No generated client package is present. {StreamingContentLoader.MissingPackageHint}");
        }

        byte[] manifest = File.ReadAllBytes(manifestPath);
        var files = ClientContentParser
            .ReadFileList(manifest, out string _)
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(folder, name)));

        ClientContent? content = ClientContentParser.Parse(manifest, files, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(content!.TryGetMap(new MapDefinitionId("map.training_ground"), out ClientMap? map), Is.True);
        Assert.That(map!.Navigation.Columns, Is.EqualTo(48));
        Assert.That(map.Navigation.CanOccupy(0f, 0f), Is.True, "players spawn at the origin");
        Assert.That(MapSceneResolver.TryResolve(map.SceneKey, out string _), Is.True);
        Assert.That(content.TryGetMap(new MapDefinitionId("map.training_field"), out ClientMap? field), Is.True);
        Assert.That(MapSceneResolver.TryResolve(field!.SceneKey, out string fieldScene), Is.True);
        Assert.That(fieldScene, Is.EqualTo("11_TrainingField"));
        Assert.That(content.TryGetJob(new JobDefinitionId("job.adventurer"), out ClientJob? job), Is.True);
        Assert.That(job!.PrefabKey, Is.EqualTo("character_adventurer"));
        Assert.That(
            content.TryGetMonster(new MonsterDefinitionId("monster.training_slime"), out ClientMonster? monster),
            Is.True);
        Assert.That(monster!.PrefabKey, Is.EqualTo("monster_training_slime"));
        Assert.That(content.TryGetItem(new ItemDefinitionId("item.material.slime_gel"), out ClientItem? item), Is.True);
        Assert.That(item!.ModelKey, Is.EqualTo("pickup_slime_gel"));
        Assert.That(content.TryGetNpc(new NpcDefinitionId("npc.gate_warden"), out ClientNpc? warden), Is.True);
        Assert.That((warden!.DisplayName, warden.PrefabKey), Is.EqualTo(("Gate Warden", "npc_gate_warden")));
        Assert.That(
            content.TryGetQuest(new QuestDefinitionId("quest.crawler_hunt"), out ClientQuest? hunt),
            Is.True);
        Assert.That(hunt!.DisplayName, Is.EqualTo("Crawler Hunt"));
    }

    [Test]
    public void Parse_ValidPackage_BuildsTheMapAndItsGrid()
    {
        var package = new Package(Maps);

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Version, Is.EqualTo(package.Version));
        Assert.That(content.TryGetMap(new MapDefinitionId("map.training_ground"), out ClientMap? map), Is.True);
        Assert.That(map!.DisplayName, Is.EqualTo("Training Ground"));
        Assert.That(map.SceneKey, Is.EqualTo("map_training_ground"));
        NavigationGrid grid = map.Navigation;
        Assert.That(grid.Columns, Is.EqualTo(3));
        Assert.That(grid.Rows, Is.EqualTo(2));
        Assert.That(grid.OriginX, Is.EqualTo(-1f));
        Assert.That(grid.OriginZ, Is.EqualTo(-2f));
        Assert.That(grid.GetCell(0, 0).Surface, Is.EqualTo(NavigationSurface.Wall), "row 0 is the southern edge");
        Assert.That(grid.GetCell(2, 0).Axis, Is.EqualTo(RampAxis.X));
        Assert.That(grid.GetCell(2, 0).HeightAtMax, Is.EqualTo(0.25f));
        Assert.That(grid.GetCell(2, 1).Surface, Is.EqualTo(NavigationSurface.Wall));
    }

    [Test]
    public void Parse_ValidPackage_FindsNoUnknownDefinition()
    {
        var package = new Package(Maps);
        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string _);

        Assert.That(content!.TryGetJob(new JobDefinitionId("job.unknown"), out ClientJob? job), Is.False);
        Assert.That(job, Is.Null);
        Assert.That(
            content.TryGetMonster(new MonsterDefinitionId("monster.unknown"), out ClientMonster? monster),
            Is.False);
        Assert.That(monster, Is.Null);
        Assert.That(content.TryGetItem(new ItemDefinitionId("item.unknown"), out ClientItem? item), Is.False);
        Assert.That(item, Is.Null);
    }

    [Test]
    public void Parse_ValidPackage_ReadsJobsMonstersAndItems()
    {
        var package = new Package(Maps);

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(content!.Jobs.Count(), Is.EqualTo(1));
        Assert.That(content.Monsters.Count(), Is.EqualTo(1));
        Assert.That(content.Items.Count(), Is.EqualTo(1));
        Assert.That(content.TryGetJob(new JobDefinitionId("job.adventurer"), out ClientJob? job), Is.True);
        Assert.That(job!.DisplayName, Is.EqualTo("Adventurer"));
        Assert.That(job.PrefabKey, Is.EqualTo("character_adventurer"));
        Assert.That(
            content.TryGetMonster(new MonsterDefinitionId("monster.training_slime"), out ClientMonster? monster),
            Is.True);
        Assert.That(monster!.DisplayName, Is.EqualTo("Training Slime"));
        Assert.That(monster.PrefabKey, Is.EqualTo("monster_training_slime"));
        Assert.That(monster.IconKey, Is.EqualTo("monster_training_slime_icon"));
        Assert.That(monster.ProjectileKey, Is.Empty, "no projectile when the package names none");
        Assert.That(content.TryGetItem(new ItemDefinitionId("item.material.slime_gel"), out ClientItem? item), Is.True);
        Assert.That(item!.DisplayName, Is.EqualTo("Slime Gel"));
        Assert.That(item.Type, Is.EqualTo(ItemType.Material));
        Assert.That(item.ModelKey, Is.EqualTo("pickup_slime_gel"));
        Assert.That(item.IconKey, Is.EqualTo("item_slime_gel"));
        Assert.That(content.TryGetSkill(new SkillDefinitionId("skill.strike"), out ClientSkill? strike), Is.True);
        Assert.That(strike!.DisplayName, Is.EqualTo("Strike"));
        Assert.That(strike.TargetType, Is.EqualTo(SkillTargetType.Enemy));
        Assert.That(strike.IconKey, Is.EqualTo("skill_strike"));
        Assert.That(strike.Description, Is.EqualTo("Hits hard."));
        Assert.That(strike.ProjectileKey, Is.EqualTo("projectile_strike"));
        Assert.That(content.TryGetSkill(new SkillDefinitionId("skill.first_aid"), out ClientSkill? aid), Is.True);
        Assert.That(aid!.TargetType, Is.EqualTo(SkillTargetType.Self));
        Assert.That(aid.Description, Is.Empty, "a description is optional");
        Assert.That(aid.ProjectileKey, Is.Empty, "a projectile is optional");
        Assert.That(content.Skills.Count(), Is.EqualTo(2));
        Assert.That(content.TryGetStatusEffect(new StatusDefinitionId("status.focus"), out ClientStatusEffect? focus),
            Is.True);
        Assert.That((focus!.DisplayName, focus.IconKey), Is.EqualTo(("Focus", "status_focus")));
        Assert.That(content.TryGetStatusEffect(new StatusDefinitionId("status.plain"), out ClientStatusEffect? plain),
            Is.True);
        Assert.That(plain!.IconKey, Is.Null, "an icon is optional");
    }

    [Test]
    public void Parse_ValidPackage_ServesTheGridThroughTheMapProvider()
    {
        var package = new Package(Maps);
        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string _);

        bool found = content!.TryGetNavigation(new MapDefinitionId("map.training_ground"), out NavigationGrid? grid);
        bool missing = content.TryGetNavigation(new MapDefinitionId("map.elsewhere"), out NavigationGrid? none);

        Assert.That(found, Is.True);
        Assert.That(grid, Is.Not.Null);
        Assert.That(missing, Is.False);
        Assert.That(none, Is.Null);
    }

    [Test]
    public void Parse_WhenAFileWasEdited_IsRefused()
    {
        var package = new Package(Maps);
        package.Files[ClientContentParser.MapsFile] = Encoding.UTF8.GetBytes(Maps.Replace("\"abc\"", "\"bbc\""));

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("maps.json").And.Contain("does not match"));
    }

    [Test]
    public void Parse_WhenAListedFileIsAbsent_IsRefused()
    {
        var package = new Package(Maps);
        package.Files.Remove("items.json");

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("items.json").And.Contain("missing"));
    }

    [Test]
    public void Parse_WhenTheVersionDoesNotBelongToTheFiles_IsRefused()
    {
        var package = new Package(Maps, "0123456789abcdef");

        ClientContent? content = ClientContentParser.Parse(package.Manifest, package.Files, out string error);

        Assert.That(content, Is.Null);
        Assert.That(error, Does.Contain("content version"));
    }

    [Test]
    public void ReadFileList_ForAValidManifest_ListsItsFiles()
    {
        var package = new Package(Maps);

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(package.Manifest, out string error);

        Assert.That(error, Is.Empty);
        Assert.That(
            names,
            Is.EquivalentTo(
                new[]
                {
                    "items.json", "jobs.json", "maps.json", "monsters.json", "npcs.json", "quests.json",
                    "skills.json", "status-effects.json"
                }));
    }
}
}
