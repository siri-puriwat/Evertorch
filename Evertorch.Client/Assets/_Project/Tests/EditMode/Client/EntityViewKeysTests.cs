using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class EntityViewKeysTests
{
    private static ClientContent CreateContent()
    {
        var job = new ClientJob(new JobDefinitionId("job.a"), "A", "character_a");
        var monster = new ClientMonster(new MonsterDefinitionId("monster.a"), "A", "monster_a", "monster_a_icon");
        var item = new ClientItem(new ItemDefinitionId("item.material.a"), "A", ItemType.Material, "pickup_a",
            "item_a_icon");
        var npc = new ClientNpc(new NpcDefinitionId("npc.a"), "A", "npc_a", Color.blue);
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob> { { job.Id, job } },
            new Dictionary<MonsterDefinitionId, ClientMonster> { { monster.Id, monster } },
            new Dictionary<ItemDefinitionId, ClientItem> { { item.Id, item } },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>(),
            new Dictionary<NpcDefinitionId, ClientNpc> { { npc.Id, npc } });
    }

    [TestCase(EntityKind.Monster, "monster.unknown")]
    [TestCase(EntityKind.ItemDrop, "item.material.unknown")]
    [TestCase(EntityKind.ItemDrop, "monster.a")]
    [TestCase(EntityKind.Monster, "job.a")]
    [TestCase(EntityKind.Player, "monster.a")]
    [TestCase(EntityKind.None, "job.a")]
    [TestCase(EntityKind.Npc, "npc.unknown")]
    [TestCase(EntityKind.Npc, "monster.a")]
    public void ForEntity_WhenTheContentDoesNotKnowIt_IsEmpty(EntityKind kind, string definitionId)
    {
        Assert.That(EntityViewKeys.ForEntity(CreateContent(), kind, definitionId), Is.Empty);
    }

    [Test]
    public void BodyTintOf_AKnownNpc_IsItsTint_AndAnUnknownOneNone()
    {
        ClientContent content = CreateContent();

        Assert.That(EntityViewKeys.BodyTintOf(content, EntityKind.Npc, "npc.a"), Is.EqualTo((Color?)Color.blue));
        Assert.That(EntityViewKeys.BodyTintOf(content, EntityKind.Npc, "npc.unknown"), Is.Null);
        Assert.That(EntityViewKeys.BodyTintOf(content, EntityKind.Player, "npc.a"), Is.Null);
        Assert.That(EntityViewKeys.BodyTintOf(content, EntityKind.Monster, "monster.a"), Is.Null, "its model's own");
        Assert.That(EntityViewKeys.BodyTintOf(null, EntityKind.Npc, "npc.a"), Is.Null);
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
    public void ForEntity_ForAKnownNpc_UsesItsPrefabKey()
    {
        Assert.That(EntityViewKeys.ForEntity(CreateContent(), EntityKind.Npc, "npc.a"), Is.EqualTo("npc_a"));
    }

    [Test]
    public void ForEntity_ForAKnownPlayerJob_UsesTheJobPrefabKey()
    {
        Assert.That(EntityViewKeys.ForEntity(CreateContent(), EntityKind.Player, "job.a"), Is.EqualTo("character_a"));
    }

    [Test]
    public void MonsterOf_AKnownMonster_IsItsDefinition_AndAnyOtherEntityNone()
    {
        ClientContent content = CreateContent();

        Assert.That(
            EntityViewKeys.MonsterOf(content, EntityKind.Monster, "monster.a")?.Id,
            Is.EqualTo(new MonsterDefinitionId("monster.a")));
        Assert.That(EntityViewKeys.MonsterOf(content, EntityKind.Monster, "monster.unknown"), Is.Null);
        Assert.That(EntityViewKeys.MonsterOf(content, EntityKind.Npc, "monster.a"), Is.Null);
        Assert.That(EntityViewKeys.MonsterOf(null, EntityKind.Monster, "monster.a"), Is.Null);
    }
}
}
