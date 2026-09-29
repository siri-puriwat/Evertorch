using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class DefinitionShapeTests
{
    private static JobDefinition Adventurer()
    {
        return new JobDefinition(
            new JobDefinitionId("job.adventurer"),
            "Adventurer",
            new PrimaryStats(5, 6, 7, 8, 9, 10),
            60,
            8,
            20,
            3,
            44,
            5.0,
            new MapDefinitionId("map.training_ground"),
            new SkillDefinitionId("skill.basic_attack"),
            new ExperienceDefinitionId("experience.adventurer"),
            new ExperienceDefinitionId("experience.adventurer_job"),
            new[] { new SkillDefinitionId("skill.strike") },
            new[] { WeaponType.Sword, WeaponType.Staff });
    }

    private static JobDefinition FirstJobOf(JobDefinition baseJob, ExperienceTableDefinition table)
    {
        return JobDefinition.FirstJob(
            new JobDefinitionId("job.vanguard"),
            "Vanguard",
            baseJob,
            table,
            60,
            14,
            20,
            3,
            new ExperienceDefinitionId("experience.first_job"),
            new SkillDefinitionId[0],
            new[] { WeaponType.Sword });
    }

    [Test]
    public void ExperienceTableDefinition_WithValues_ExposesThem()
    {
        var table = new ExperienceTableDefinition(new ExperienceDefinitionId("experience.adventurer"),
            new[] { 30, 50 });

        Assert.That(table.Id.Value, Is.EqualTo("experience.adventurer"));
        Assert.That(table.Levels, Is.EqualTo(new[] { 30, 50 }));
    }

    [Test]
    public void ItemDefinition_WithValues_ExposesThem()
    {
        var item = new ItemDefinition(
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
    public void JobDefinition_FirstJobOfAFirstJobOrWithAnotherTable_Throws()
    {
        JobDefinition adventurer = Adventurer();
        var table = new ExperienceTableDefinition(new ExperienceDefinitionId("experience.adventurer_job"),
            new[] { 30 });
        var other = new ExperienceTableDefinition(new ExperienceDefinitionId("experience.first_job"), new[] { 250 });
        JobDefinition first = FirstJobOf(adventurer, table);

        Action ofFirstJob = () => FirstJobOf(first, other);
        Action withAnotherTable = () => FirstJobOf(adventurer, other);

        Assert.That(ofFirstJob, Throws.ArgumentException);
        Assert.That(withAnotherTable, Throws.ArgumentException);
    }

    // A first job takes its base job's starting statistics, map, base table, basic attack, and movement, keeps its own
    // values, and learns from the base tree followed by its own; it carries the base job's job cap less one in points
    // (Content Pipeline §4; Gameplay Systems §2.1).
    [Test]
    public void JobDefinition_FirstJob_TakesItsBaseJobsValuesAndWholeTree()
    {
        JobDefinition adventurer = Adventurer();
        var table = new ExperienceTableDefinition(
            new ExperienceDefinitionId("experience.adventurer_job"),
            new[] { 30, 50, 80, 120, 170, 230, 300, 380, 470 });

        var vanguard = JobDefinition.FirstJob(
            new JobDefinitionId("job.vanguard"),
            "Vanguard",
            adventurer,
            table,
            60,
            14,
            20,
            3,
            new ExperienceDefinitionId("experience.first_job"),
            new[] { new SkillDefinitionId("skill.heavy_blow") },
            new[] { WeaponType.Sword });

        Assert.That((vanguard.Id.Value, vanguard.DisplayName), Is.EqualTo(("job.vanguard", "Vanguard")));
        Assert.That(vanguard.BaseJob, Is.EqualTo(adventurer.Id));
        Assert.That(vanguard.StartingStats, Is.EqualTo(adventurer.StartingStats));
        Assert.That(vanguard.StartingMap, Is.EqualTo(adventurer.StartingMap));
        Assert.That(vanguard.ExperienceTable, Is.EqualTo(adventurer.ExperienceTable));
        Assert.That(vanguard.BasicAttack, Is.EqualTo(adventurer.BasicAttack));
        Assert.That(vanguard.BaseSpeed, Is.EqualTo(adventurer.BaseSpeed));
        Assert.That(vanguard.UnarmedAttackSpeedPenalty, Is.EqualTo(adventurer.UnarmedAttackSpeedPenalty));
        Assert.That(
            (vanguard.HealthBase, vanguard.HealthPerLevel, vanguard.SpiritBase, vanguard.SpiritPerLevel),
            Is.EqualTo((60, 14, 20, 3)));
        Assert.That(vanguard.JobExperienceTable.Value, Is.EqualTo("experience.first_job"));
        Assert.That(vanguard.Skills.Select(skill => skill.Value), Is.EqualTo(new[] { "skill.heavy_blow" }));
        Assert.That(
            vanguard.Tree.Select(skill => skill.Value),
            Is.EqualTo(new[] { "skill.strike", "skill.heavy_blow" }));
        Assert.That(vanguard.CarriedSkillPoints, Is.EqualTo(9), "a table of 9 caps the Adventurer at 10");
        Assert.That((vanguard.CanWield(WeaponType.Sword), vanguard.CanWield(WeaponType.Staff)),
            Is.EqualTo((true, false)));
    }

    [Test]
    public void JobDefinition_WithValues_ExposesThem()
    {
        var stats = new PrimaryStats(5, 6, 7, 8, 9, 10);
        var job = new JobDefinition(
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
            new SkillDefinitionId("skill.basic_attack"),
            new ExperienceDefinitionId("experience.adventurer"),
            new ExperienceDefinitionId("experience.adventurer_job"),
            new[] { new SkillDefinitionId("skill.strike") },
            new[] { WeaponType.Sword, WeaponType.Staff });

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
        Assert.That(job.ExperienceTable.Value, Is.EqualTo("experience.adventurer"));
        Assert.That(job.JobExperienceTable.Value, Is.EqualTo("experience.adventurer_job"));
        Assert.That(job.Skills, Is.EqualTo(new[] { new SkillDefinitionId("skill.strike") }));
        Assert.That(job.Weapons, Is.EqualTo(new[] { WeaponType.Sword, WeaponType.Staff }));
        Assert.That((job.BaseJob, job.CarriedSkillPoints), Is.EqualTo(((JobDefinitionId?)null, 0)), "a base job");
        Assert.That(job.Tree, Is.EqualTo(job.Skills), "its tree is its own skills");
    }

    [Test]
    public void MapDefinition_WithNpcs_ExposesThem_AndPlacesNoneByDefault()
    {
        var placement = new NpcPlacement(
            new NpcDefinitionId("npc.quartermaster"),
            new WorldPosition(-3.5f, 0f, 4.5f),
            new WorldDirection(0.6f, -0.8f));
        NavigationGrid navigation = TestGrids.FromRows("..", "..");
        var withNpc = new MapDefinition(
            new MapDefinitionId("map.training_ground"),
            "Training Ground",
            default,
            new WorldDirection(0f, 1f),
            new MonsterSpawn[0],
            navigation,
            null,
            new[] { placement });
        var without = new MapDefinition(
            new MapDefinitionId("map.training_field"),
            "Training Field",
            default,
            new WorldDirection(0f, 1f),
            new MonsterSpawn[0],
            navigation);

        Assert.That(withNpc.Npcs, Is.EqualTo(new[] { placement }));
        Assert.That(without.Npcs, Is.Empty);
        Assert.That(placement.Npc.Value, Is.EqualTo("npc.quartermaster"));
        Assert.That(placement.Position, Is.EqualTo(new WorldPosition(-3.5f, 0f, 4.5f)));
        Assert.That(placement.Facing, Is.EqualTo(new WorldDirection(0.6f, -0.8f)));
    }

    [Test]
    public void MapDefinition_WithValues_ExposesThem()
    {
        var spawn = new MonsterSpawn(
            new MonsterDefinitionId("monster.training_slime"),
            new WorldPosition(12f, 0f, 12f),
            6.0,
            4,
            8000);
        NavigationGrid navigation = TestGrids.FromRows("..", "..");
        var map = new MapDefinition(
            new MapDefinitionId("map.training_ground"),
            "Training Ground",
            new WorldPosition(1f, 2f, 3f),
            new WorldDirection(0f, 1f),
            new[] { spawn },
            navigation);

        Assert.That(map.Id.Value, Is.EqualTo("map.training_ground"));
        Assert.That(map.DisplayName, Is.EqualTo("Training Ground"));
        Assert.That(map.SpawnPosition, Is.EqualTo(new WorldPosition(1f, 2f, 3f)));
        Assert.That(map.SpawnFacing, Is.EqualTo(new WorldDirection(0f, 1f)));
        Assert.That(map.MonsterSpawns, Is.EqualTo(new[] { spawn }));
        Assert.That(map.Navigation, Is.SameAs(navigation));
        Assert.That(spawn.Monster.Value, Is.EqualTo("monster.training_slime"));
        Assert.That(spawn.Center, Is.EqualTo(new WorldPosition(12f, 0f, 12f)));
        Assert.That(spawn.Radius, Is.EqualTo(6.0));
        Assert.That(spawn.Count, Is.EqualTo(4));
        Assert.That(spawn.RespawnMs, Is.EqualTo(8000));
    }

    [Test]
    public void MapDefinition_WithoutNavigation_Throws()
    {
        Action create = () => _ = new MapDefinition(
            new MapDefinitionId("map.training_ground"),
            "Training Ground",
            default,
            new WorldDirection(0f, 1f),
            new MonsterSpawn[0],
            null!);

        Assert.That(create, Throws.ArgumentNullException);
    }

    [Test]
    public void MonsterDefinition_WithValues_ExposesThem()
    {
        var drop = new MonsterDrop(new ItemDefinitionId("item.material.slime_gel"), 0.35, 1, 2);
        var monster = new MonsterDefinition(
            new MonsterDefinitionId("monster.training_slime"),
            "Training Slime",
            1,
            50,
            7,
            2,
            155,
            102,
            20,
            4.0,
            1.5,
            1200,
            MonsterBehavior.Passive,
            6.0,
            12.0,
            6.0,
            4000,
            5000,
            100,
            0.75,
            10,
            12,
            new[] { drop },
            new[] { new MonsterSkill(new SkillDefinitionId("skill.spark_bolt"), 0.25) });

        Assert.That(monster.Id.Value, Is.EqualTo("monster.training_slime"));
        Assert.That(monster.DisplayName, Is.EqualTo("Training Slime"));
        Assert.That(monster.RoamRadius, Is.EqualTo(6.0));
        Assert.That(monster.IdlePauseMinMs, Is.EqualTo(4000));
        Assert.That(monster.IdlePauseMaxMs, Is.EqualTo(5000));
        Assert.That(monster.ScanIntervalMs, Is.EqualTo(100));
        Assert.That((monster.MagicAttack, monster.KeepDistance), Is.EqualTo((20, 0.75)));
        Assert.That(monster.Skills.Single().Skill.Value, Is.EqualTo("skill.spark_bolt"));
        Assert.That(monster.Skills.Single().Chance, Is.EqualTo(0.25));
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
        Assert.That((monster.BaseExperience, monster.JobExperience), Is.EqualTo((10, 12)));
        Assert.That(monster.Drops, Is.EqualTo(new[] { drop }));
        Assert.That(drop.Item.Value, Is.EqualTo("item.material.slime_gel"));
        Assert.That(drop.Chance, Is.EqualTo(0.35));
        Assert.That(drop.MinAmount, Is.EqualTo(1));
        Assert.That(drop.MaxAmount, Is.EqualTo(2));
    }

    [Test]
    public void NpcDefinition_WithAShopOrAReset_ExposesThem_AndWithoutHasNone()
    {
        var potion = new ShopEntry(new ItemDefinitionId("item.consumable.minor_health"), 20);
        var quartermaster =
            new NpcDefinition(new NpcDefinitionId("npc.quartermaster"), "Quartermaster", new[] { potion });
        var warden = new NpcDefinition(new NpcDefinitionId("npc.gate_warden"), "Gate Warden");
        var guildmaster = new NpcDefinition(new NpcDefinitionId("npc.guildmaster"), "Guildmaster", offersReset: true);

        Assert.That(quartermaster.Id.Value, Is.EqualTo("npc.quartermaster"));
        Assert.That(quartermaster.DisplayName, Is.EqualTo("Quartermaster"));
        Assert.That(quartermaster.Shop, Is.EqualTo(new[] { potion }));
        Assert.That(quartermaster.HasShop, Is.True);
        Assert.That((potion.Item.Value, potion.Price), Is.EqualTo(("item.consumable.minor_health", 20)));
        Assert.That(warden.Shop, Is.Empty);
        Assert.That(warden.HasShop, Is.False);
        Assert.That((quartermaster.OffersReset, warden.OffersReset), Is.EqualTo((false, false)));
        Assert.That((guildmaster.OffersReset, guildmaster.HasShop), Is.EqualTo((true, false)));
    }

    [Test]
    public void QuestDefinition_WithValues_ExposesThem()
    {
        var quest = new QuestDefinition(
            new QuestDefinitionId("quest.crawler_hunt"),
            "Crawler Hunt",
            new NpcDefinitionId("npc.gate_warden"),
            new MonsterDefinitionId("monster.forest_crawler"),
            5,
            150,
            160,
            100);

        Assert.That(quest.Id.Value, Is.EqualTo("quest.crawler_hunt"));
        Assert.That(quest.DisplayName, Is.EqualTo("Crawler Hunt"));
        Assert.That(quest.Giver.Value, Is.EqualTo("npc.gate_warden"));
        Assert.That(quest.Monster.Value, Is.EqualTo("monster.forest_crawler"));
        Assert.That(
            (quest.Count, quest.BaseExperience, quest.JobExperience, quest.Currency),
            Is.EqualTo((5, 150, 160, 100)));
    }

    [Test]
    public void SkillDefinition_WithLevels_ExposesThemAndHoldsALevelToThem()
    {
        var first = new SkillLevel(8, 100, 200, 500, 2000, SkillEffect.Damage(130));
        var second = new SkillLevel(9, 100, 200, 500, 2000, SkillEffect.Damage(145));
        var requires = new SkillRequirement(new SkillDefinitionId("skill.first_aid"), 1);
        var skill = new SkillDefinition(
            new SkillDefinitionId("skill.strike"),
            "Strike",
            SkillTargetType.Enemy,
            SkillDamageType.Physical,
            1.5,
            SkillPaymentPoint.CastStart,
            new[] { first, second },
            requires);

        Assert.That(skill.Id.Value, Is.EqualTo("skill.strike"));
        Assert.That(skill.DisplayName, Is.EqualTo("Strike"));
        Assert.That(skill.TargetType, Is.EqualTo(SkillTargetType.Enemy));
        Assert.That(skill.DamageType, Is.EqualTo(SkillDamageType.Physical));
        Assert.That(skill.Range, Is.EqualTo(1.5));
        Assert.That(skill.SpPaidAt, Is.EqualTo(SkillPaymentPoint.CastStart));
        Assert.That((skill.MaxLevel, skill.HasEffect), Is.EqualTo((2, true)));
        Assert.That(skill.Requires, Is.SameAs(requires));
        Assert.That((requires.Skill.Value, requires.Level), Is.EqualTo(("skill.first_aid", 1)));
        Assert.That(
            (first.SpCost, first.FixedCastMs, first.VariableCastMs, first.AfterCastDelayMs, first.CooldownMs),
            Is.EqualTo((8, 100, 200, 500, 2000)));
        Assert.That(skill.ValuesAt(1), Is.SameAs(first));
        Assert.That(skill.ValuesAt(2).Effect.DamageRatioPercent, Is.EqualTo(145));
        Assert.That(skill.ValuesAt(0), Is.SameAs(first), "held to level 1");
        Assert.That(skill.ValuesAt(7), Is.SameAs(second), "held to the maximum");
    }

    [Test]
    public void SkillDefinition_WithoutLevels_HasNoEffect()
    {
        var basicAttack = new SkillDefinition(
            new SkillDefinitionId("skill.basic_attack"),
            "Basic Attack",
            SkillTargetType.Enemy,
            SkillDamageType.Physical,
            1.5,
            SkillPaymentPoint.Resolution,
            Array.Empty<SkillLevel>());
        Action values = () => basicAttack.ValuesAt(1);

        Assert.That((basicAttack.MaxLevel, basicAttack.HasEffect), Is.EqualTo((0, false)));
        Assert.That(basicAttack.Requires, Is.Null);
        Assert.That(values, Throws.InvalidOperationException);
    }

    [Test]
    public void SkillEffect_ForAStatus_NamesItItsDurationAndItsStrength()
    {
        var percent = new StatPercentages(0, 40, 0, 0, 40, 0);
        var focus = SkillEffect.StatusEffect(new StatusDefinitionId("status.focus"), 60_000, percent);
        Action noStatus = () => SkillEffect.StatusEffect(default, 60_000, percent);
        Action noDuration = () => SkillEffect.StatusEffect(new StatusDefinitionId("status.focus"), 0, percent);

        Assert.That(
            (focus.Kind, focus.Status.Value, focus.StatusDurationMs, focus.HealHp),
            Is.EqualTo((SkillEffectKind.Status, "status.focus", 60_000, 0)));
        Assert.That(focus.StatPercent, Is.EqualTo(percent));
        Assert.That(noStatus, Throws.ArgumentException);
        Assert.That(noDuration, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SkillEffect_OfEachKind_KeepsOnlyItsOwnValue()
    {
        var heal = SkillEffect.Heal(15);
        Action noRatio = () => SkillEffect.Damage(0);
        Action noHeal = () => SkillEffect.Heal(0);

        Assert.That((heal.Kind, heal.HealHp, heal.DamageRatioPercent), Is.EqualTo((SkillEffectKind.Heal, 15, 0)));
        Assert.That(noRatio, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noHeal, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void StatPercentages_OfTwoEffects_AddUp_AndNeverGoNegative()
    {
        StatPercentages sum = new StatPercentages(1, 2, 3, 4, 5, 6).Plus(new StatPercentages(10, 0, 0, 0, 10, 0));
        Action negative = () => _ = new StatPercentages(0, -1, 0, 0, 0, 0);

        Assert.That(sum, Is.EqualTo(new StatPercentages(11, 2, 3, 4, 15, 6)));
        Assert.That(negative, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void StatusEffectDefinition_WithValues_ExposesThem()
    {
        var focus = new StatusEffectDefinition(new StatusDefinitionId("status.focus"), "Focus");

        Assert.That(focus.Id.Value, Is.EqualTo("status.focus"));
        Assert.That(focus.DisplayName, Is.EqualTo("Focus"));
    }
}
}
