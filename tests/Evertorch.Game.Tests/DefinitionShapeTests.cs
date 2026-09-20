using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class DefinitionShapeTests
{
    [Test]
    public void ItemDefinition_WithValues_ExposesThem()
    {
        ItemDefinition item = new ItemDefinition(
            new ItemDefinitionId("item.material.slime_gel"),
            "Slime Gel",
            ItemType.Material,
            999,
            1,
            4);

        Assert.That(item.Id.Value, Is.EqualTo("item.material.slime_gel"));
        Assert.That(item.DisplayName, Is.EqualTo("Slime Gel"));
        Assert.That(item.Type, Is.EqualTo(ItemType.Material));
        Assert.That(item.StackLimit, Is.EqualTo(999));
        Assert.That(item.Weight, Is.EqualTo(1));
        Assert.That(item.SellPrice, Is.EqualTo(4));
    }

    [Test]
    public void MonsterDefinition_WithValues_ExposesThem()
    {
        MonsterDrop drop = new MonsterDrop(new ItemDefinitionId("item.material.slime_gel"), 0.35, 1, 2);
        MonsterDefinition monster = new MonsterDefinition(
            new MonsterDefinitionId("monster.training_slime"),
            "Training Slime",
            1,
            50,
            7,
            2,
            155,
            102,
            4.0,
            1.5,
            1200,
            MonsterBehavior.Passive,
            6.0,
            12.0,
            new[] { drop });

        Assert.That(monster.Id.Value, Is.EqualTo("monster.training_slime"));
        Assert.That(monster.DisplayName, Is.EqualTo("Training Slime"));
        Assert.That(monster.Level, Is.EqualTo(1));
        Assert.That(monster.Hp, Is.EqualTo(50));
        Assert.That(monster.PhysicalAttack, Is.EqualTo(7));
        Assert.That(monster.PhysicalDefense, Is.EqualTo(2));
        Assert.That(monster.Hit, Is.EqualTo(155));
        Assert.That(monster.Flee, Is.EqualTo(102));
        Assert.That(monster.BaseSpeed, Is.EqualTo(4.0));
        Assert.That(monster.AttackRange, Is.EqualTo(1.5));
        Assert.That(monster.AttackIntervalMs, Is.EqualTo(1200));
        Assert.That(monster.Behavior, Is.EqualTo(MonsterBehavior.Passive));
        Assert.That(monster.PerceptionRadius, Is.EqualTo(6.0));
        Assert.That(monster.LeashRadius, Is.EqualTo(12.0));
        Assert.That(monster.Drops, Is.EqualTo(new[] { drop }));
        Assert.That(drop.Item.Value, Is.EqualTo("item.material.slime_gel"));
        Assert.That(drop.Chance, Is.EqualTo(0.35));
        Assert.That(drop.MinAmount, Is.EqualTo(1));
        Assert.That(drop.MaxAmount, Is.EqualTo(2));
    }

    [Test]
    public void SkillDefinition_WithValues_ExposesThem()
    {
        SkillDefinition skill = new SkillDefinition(
            new SkillDefinitionId("skill.basic_attack"),
            "Basic Attack",
            SkillTargetType.Enemy,
            SkillDamageType.Physical,
            1.5);

        Assert.That(skill.Id.Value, Is.EqualTo("skill.basic_attack"));
        Assert.That(skill.DisplayName, Is.EqualTo("Basic Attack"));
        Assert.That(skill.TargetType, Is.EqualTo(SkillTargetType.Enemy));
        Assert.That(skill.DamageType, Is.EqualTo(SkillDamageType.Physical));
        Assert.That(skill.Range, Is.EqualTo(1.5));
    }

    [Test]
    public void JobDefinition_WithValues_ExposesThem()
    {
        PrimaryStats stats = new PrimaryStats(5, 6, 7, 8, 9, 10);
        JobDefinition job = new JobDefinition(
            new JobDefinitionId("job.adventurer"),
            "Adventurer",
            stats,
            60,
            8,
            20,
            3,
            44,
            5.0,
            new MapDefinitionId("map.training_ground"),
            new SkillDefinitionId("skill.basic_attack"));

        Assert.That(job.Id.Value, Is.EqualTo("job.adventurer"));
        Assert.That(job.DisplayName, Is.EqualTo("Adventurer"));
        Assert.That(job.StartingStats, Is.EqualTo(stats));
        Assert.That(job.HealthBase, Is.EqualTo(60));
        Assert.That(job.HealthPerLevel, Is.EqualTo(8));
        Assert.That(job.SpiritBase, Is.EqualTo(20));
        Assert.That(job.SpiritPerLevel, Is.EqualTo(3));
        Assert.That(job.UnarmedAttackSpeedPenalty, Is.EqualTo(44));
        Assert.That(job.BaseSpeed, Is.EqualTo(5.0));
        Assert.That(job.StartingMap.Value, Is.EqualTo("map.training_ground"));
        Assert.That(job.BasicAttack.Value, Is.EqualTo("skill.basic_attack"));
    }

    [Test]
    public void MapDefinition_WithValues_ExposesThem()
    {
        MonsterSpawn spawn = new MonsterSpawn(
            new MonsterDefinitionId("monster.training_slime"),
            new WorldPosition(12f, 0f, 12f),
            6.0,
            4,
            8000);
        MapDefinition map = new MapDefinition(
            new MapDefinitionId("map.training_ground"),
            "Training Ground",
            new WorldPosition(1f, 2f, 3f),
            new WorldDirection(0f, 1f),
            new[] { spawn });

        Assert.That(map.Id.Value, Is.EqualTo("map.training_ground"));
        Assert.That(map.DisplayName, Is.EqualTo("Training Ground"));
        Assert.That(map.SpawnPosition, Is.EqualTo(new WorldPosition(1f, 2f, 3f)));
        Assert.That(map.SpawnFacing, Is.EqualTo(new WorldDirection(0f, 1f)));
        Assert.That(map.MonsterSpawns, Is.EqualTo(new[] { spawn }));
        Assert.That(spawn.Monster.Value, Is.EqualTo("monster.training_slime"));
        Assert.That(spawn.Center, Is.EqualTo(new WorldPosition(12f, 0f, 12f)));
        Assert.That(spawn.Radius, Is.EqualTo(6.0));
        Assert.That(spawn.Count, Is.EqualTo(4));
        Assert.That(spawn.RespawnMs, Is.EqualTo(8000));
    }
}
}
