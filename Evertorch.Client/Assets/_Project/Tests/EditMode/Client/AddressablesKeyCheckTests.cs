using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client.Editor;
using Evertorch.Game;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class AddressablesKeyCheckTests
{
    private static readonly string[] EnabledScenes = { "00_Bootstrap", "10_TrainingGround", "11_TrainingField" };

    private static ClientContent CreateContent(
        string jobPrefab = "character_adventurer",
        string monsterPrefab = "monster_training_slime",
        string itemModel = "pickup_slime_gel",
        string sceneKey = "map_training_ground")
    {
        var map = new ClientMap(new MapDefinitionId("map.a"), "A", sceneKey, ClientTestGrids.CreateYard());
        var job = new ClientJob(new JobDefinitionId("job.a"), "A", jobPrefab);
        var monster = new ClientMonster(new MonsterDefinitionId("monster.a"), "A", monsterPrefab, "monster_icon");
        var item = new ClientItem(new ItemDefinitionId("item.a"), "A", ItemType.Material, itemModel, "item_icon");
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap> { { map.Id, map } },
            new Dictionary<JobDefinitionId, ClientJob> { { job.Id, job } },
            new Dictionary<MonsterDefinitionId, ClientMonster> { { monster.Id, monster } },
            new Dictionary<ItemDefinitionId, ClientItem> { { item.Id, item } },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    private static Dictionary<string, Type?> Prefabs()
    {
        return new Dictionary<string, Type?>(StringComparer.Ordinal)
        {
            { "character_adventurer", typeof(GameObject) },
            { "monster_training_slime", typeof(GameObject) },
            { "pickup_slime_gel", typeof(GameObject) }
        };
    }

    [TestCase("job")]
    [TestCase("monster")]
    [TestCase("item")]
    public void Check_WhenARequiredKeyHasNoEntry_ReportsIt(string kind)
    {
        ClientContent content = kind switch
        {
            "job" => CreateContent("absent_key"),
            "monster" => CreateContent(monsterPrefab: "absent_key"),
            _ => CreateContent(itemModel: "absent_key")
        };

        AddressablesKeyCheckResult result = AddressablesKeyCheck.Check(content, Prefabs(), EnabledScenes);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Missing, Has.Count.EqualTo(1));
        Assert.That(result.Missing[0], Does.Contain("'absent_key'"));
        Assert.That(result.Describe(), Does.Contain("absent_key"));
    }

    [Test]
    public void Check_WhenAKeyIsNotAPrefab_ReportsIt()
    {
        Dictionary<string, Type?> addresses = Prefabs();
        addresses["monster_training_slime"] = typeof(Material);

        AddressablesKeyCheckResult result = AddressablesKeyCheck.Check(CreateContent(), addresses, EnabledScenes);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Missing[0], Does.Contain("is not a prefab"));
    }

    [Test]
    public void Check_WhenAProjectileHasNoEntry_ListsItAsOptional_ForTheClientFliesASphere()
    {
        ClientContent original = CreateContent();
        var monster = new ClientMonster(
            new MonsterDefinitionId("monster.a"),
            "A",
            "monster_training_slime",
            "monster_icon",
            "projectile_absent");
        var content = new ClientContent(
            original.Version,
            new Dictionary<MapDefinitionId, ClientMap> { { new MapDefinitionId("map.a"), original.Maps.Single() } },
            new Dictionary<JobDefinitionId, ClientJob> { { new JobDefinitionId("job.a"), original.Jobs.Single() } },
            new Dictionary<MonsterDefinitionId, ClientMonster> { { monster.Id, monster } },
            new Dictionary<ItemDefinitionId, ClientItem>
                { { new ItemDefinitionId("item.a"), original.Items.Single() } },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());

        AddressablesKeyCheckResult result = AddressablesKeyCheck.Check(content, Prefabs(), EnabledScenes);

        Assert.That(result.IsValid, Is.True);
        Assert.That(
            result.MissingOptional.Any(entry => entry.Contains("monster.a projectile 'projectile_absent'")),
            Is.True,
            string.Join(" | ", result.MissingOptional));
    }

    [Test]
    public void Check_WhenEveryRequiredKeyResolves_IsValidAndListsIconsAsOptional()
    {
        AddressablesKeyCheckResult result = AddressablesKeyCheck.Check(CreateContent(), Prefabs(), EnabledScenes);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Missing, Is.Empty);
        Assert.That(result.MissingOptional, Has.Count.EqualTo(2));
    }

    [Test]
    public void Check_WhenTheMapSceneIsNotInTheBuild_ReportsIt()
    {
        ClientContent content = CreateContent(sceneKey: "map_unknown");

        AddressablesKeyCheckResult result = AddressablesKeyCheck.Check(content, Prefabs(), EnabledScenes);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Missing[0], Does.Contain("map_unknown"));
    }

    [Test]
    public void Run_ForTheProjectAndItsGeneratedPackage_FindsEveryRequiredKey()
    {
        string manifest = Path.Combine(
            Application.streamingAssetsPath,
            StreamingContentLoader.FolderName,
            ClientContentParser.ManifestFile);
        if (!File.Exists(manifest))
        {
            Assert.Ignore($"No generated client package is present. {StreamingContentLoader.MissingPackageHint}");
        }

        AddressablesKeyCheckResult result = AddressablesKeyCheck.Run();

        Assert.That(result.PackageError, Is.Empty);
        Assert.That(result.Missing, Is.Empty, result.Describe());
    }
}
}
