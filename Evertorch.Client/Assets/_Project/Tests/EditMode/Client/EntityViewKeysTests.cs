using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class EntityViewKeysTests
{
    private static ClientContent CreateContent()
    {
        var job = new ClientJob(new JobDefinitionId("job.a"), "A", "character_a");
        var monster = new ClientMonster(new MonsterDefinitionId("monster.a"), "A", "monster_a", "monster_a_icon");
        var item = new ClientItem(new ItemDefinitionId("item.material.a"), "A", "pickup_a", "item_a_icon");
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob> { { job.Id, job } },
            new Dictionary<MonsterDefinitionId, ClientMonster> { { monster.Id, monster } },
            new Dictionary<ItemDefinitionId, ClientItem> { { item.Id, item } },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    [TestCase(EntityKind.Monster, "monster.unknown")]
    [TestCase(EntityKind.ItemDrop, "item.material.unknown")]
    [TestCase(EntityKind.ItemDrop, "monster.a")]
    [TestCase(EntityKind.Monster, "job.a")]
    [TestCase(EntityKind.Player, "monster.a")]
    [TestCase(EntityKind.None, "job.a")]
    public void ForEntity_WhenTheContentDoesNotKnowIt_IsEmpty(EntityKind kind, string definitionId)
    {
        Assert.That(EntityViewKeys.ForEntity(CreateContent(), kind, definitionId), Is.Empty);
    }

    [Test]
    public void ForEntity_ForAKnownItemDrop_UsesTheItemModelKey()
    {
        Assert.That(
            EntityViewKeys.ForEntity(CreateContent(), EntityKind.ItemDrop, "item.material.a"),
            Is.EqualTo("pickup_a"));
    }

    [Test]
    public void ForEntity_ForAKnownMonster_UsesItsPrefabKey()
    {
        string key = EntityViewKeys.ForEntity(CreateContent(), EntityKind.Monster, "monster.a");

        Assert.That(key, Is.EqualTo("monster_a"));
    }

    [Test]
    public void ForEntity_ForAKnownPlayerJob_UsesTheJobPrefabKey()
    {
        Assert.That(EntityViewKeys.ForEntity(CreateContent(), EntityKind.Player, "job.a"), Is.EqualTo("character_a"));
    }
}
}
