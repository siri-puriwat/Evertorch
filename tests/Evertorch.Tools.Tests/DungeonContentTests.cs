using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The Umbral Grotto as content (Gameplay Systems §2.1, §4.2; Prototype Content §6): the dungeon behind the
///     training field's east gate, and the Adventurer's base table to level 25. Each later line of Milestone 13 adds
///     what it places in the grotto.
/// </summary>
[TestFixture]
public sealed class DungeonContentTests
{
    private const string Grotto = "map.umbral_grotto";
    private const string Monarch = "monster.slime_monarch";

    // The boss chamber in the north-east, from 6 m to 30 m on both axes; its one way in is the corridor from the
    // crawler hall.
    private const double ChamberMin = 6d;
    private const double ChamberMax = 30d;

    // The path budget of the client's movement and of the monsters: a map of at most this many cells can always be
    // crossed in one path.
    private const int PathNodeBudget = 8192;

    private static ContentPipelineResult RepositoryContent()
    {
        ContentPipelineResult result = ContentPipeline.Run(
            Path.Combine(ContentValidationTests.RepositoryRoot(), "content"));
        Assert.That(
            result.Diagnostics,
            Is.Empty,
            string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return result;
    }

    private static MapDefinition MapOf(ContentPipelineResult result, string id)
    {
        return result.Content.Maps.Single(map => map.Definition.Id.Value == id).Definition;
    }

    private static QuestDefinition QuestOf(ContentPipelineResult result, string id)
    {
        return result.Content.Quests.Single(quest => quest.Definition.Id.Value == id).Definition;
    }

    private static MonsterDefinition MonsterOf(ContentPipelineResult result, string id)
    {
        return result.Content.Monsters.Single(monster => monster.Definition.Id.Value == id).Definition;
    }

    // The entrance hall in the south-west, the crawler hall, the wisp gallery, and the boss chamber; every room can be
    // walked to from the spawn point within the monsters' path budget.
    [TestCase(-18.5f, 15.5f, "the crawler hall")]
    [TestCase(16f, -19.5f, "the wisp gallery")]
    [TestCase(18f, 18f, "the boss chamber")]
    [TestCase(24.5f, -26.5f, "the hollow wisps' corner of the wisp gallery")]
    [TestCase(-20.5f, -26.5f, "the barrow pack's end of the entrance hall")]
    public void Run_ForRepositoryContent_LetsEveryRoomOfTheGrottoBeWalkedToFromItsSpawnPoint(
        float x,
        float z,
        string room)
    {
        MapDefinition grotto = MapOf(RepositoryContent(), Grotto);
        NavigationGrid grid = grotto.Navigation;
        var waypoints = new List<WorldPosition>();

        bool isReachable = new GridPathfinder(grid).TryFindPath(
            grotto.SpawnPosition,
            new WorldPosition(x, 0f, z),
            grid.Columns * grid.Rows,
            waypoints);

        Assert.That(grid.Columns * grid.Rows, Is.LessThanOrEqualTo(PathNodeBudget));
        Assert.That(isReachable, Is.True, room);
    }

    // The second tier: the shop buys it back but sells none of it, and only the grotto's monsters drop it.
    [Test]
    public void Run_ForRepositoryContent_DropsTheSecondTierInTheGrottoAlone_AndNoShopSellsIt()
    {
        ContentPipelineResult result = RepositoryContent();
        string[] tier = { "item.weapon.iron_sword", "item.weapon.ash_staff", "item.armor.leather" };

        var sold = result.Content.Npcs
            .SelectMany(npc => npc.Definition.Shop)
            .Select(entry => entry.Item.Value)
            .ToList();
        var dropping = result.Content.Monsters
            .Where(monster => monster.Definition.Drops.Any(drop => tier.Contains(drop.Item.Value)))
            .Select(monster => monster.Definition.Id)
            .ToList();
        var spawnedOutside = result.Content.Maps
            .Where(map => map.Definition.Id.Value != Grotto)
            .SelectMany(map => map.Definition.MonsterSpawns)
            .Select(spawn => spawn.Monster)
            .ToList();
        Assert.That(
            tier.Select(id => result.Content.Items.Single(item => item.Definition.Id.Value == id).Definition.SellPrice),
            Is.All.GreaterThan(0),
            "the shop buys it back");
        Assert.That(sold.Intersect(tier), Is.Empty, "no shop sells it");
        Assert.That(dropping, Is.Not.Empty);
        Assert.That(dropping.Intersect(spawnedOutside), Is.Empty, "no monster outside the grotto drops it");
    }

    // The Gate Warden's two quests pay as the Crawler Hunt pays for its kills: 1.2 times their base experience, as much
    // job experience, and 0.8 coins a point; the boss's on one member's share of a party of three.
    [Test]
    public void Run_ForRepositoryContent_GivesTheGrottoHuntAndTheSlimeMonarchAtTheGateWarden()
    {
        ContentPipelineResult result = RepositoryContent();
        QuestDefinition hunt = QuestOf(result, "quest.grotto_hunt");
        QuestDefinition boss = QuestOf(result, "quest.slime_monarch");
        int killed = hunt.Count * MonsterOf(result, hunt.Monster.Value).BaseExperience;
        int share = MonsterOf(result, Monarch).BaseExperience / 3;

        Assert.That(
            (hunt.DisplayName, hunt.Giver.Value, hunt.Monster.Value, hunt.Count),
            Is.EqualTo(("Grotto Hunt", "npc.gate_warden", "monster.grotto_crawler", 10)));
        Assert.That(
            (boss.DisplayName, boss.Giver.Value, boss.Monster.Value, boss.Count),
            Is.EqualTo(("The Slime Monarch", "npc.gate_warden", Monarch, 1)));
        Assert.That(
            (hunt.BaseExperience, hunt.JobExperience, hunt.Currency),
            Is.EqualTo((killed * 6 / 5, killed * 6 / 5, killed * 4 / 5)),
            "1,440, 1,440, and 960 for ten crawlers' 1,200");
        Assert.That(
            (boss.BaseExperience, boss.JobExperience, boss.Currency),
            Is.EqualTo((share * 6 / 5, share * 6 / 5, share * 4 / 5)),
            "2,400, 2,400, and 1,600 for a third of the boss's 6,000");
    }

    // Each step grows by ten more than the one before, from 30 at level 1 to 3,020 at level 24; a table of n entries
    // caps its jobs at level n + 1.
    [Test]
    public void Run_ForRepositoryContent_GrowsTheAdventurersTableToLevel25()
    {
        ContentPipelineResult result = RepositoryContent();

        IReadOnlyList<int> levels = result.Content.ExperienceTables
            .Single(table => table.Definition.Id.Value == "experience.adventurer")
            .Definition.Levels;
        Assert.That(levels, Has.Count.EqualTo(24), "the cap of 25");
        Assert.That(levels.Take(14).Sum(), Is.EqualTo(5880), "to level 15, as before");
        Assert.That(levels.Skip(14), Is.EqualTo(new[] { 1220, 1380, 1550, 1730, 1920, 2120, 2330, 2550, 2780, 3020 }));
        Assert.That(
            Enumerable.Range(2, levels.Count - 2)
                .Select(index => levels[index] - 2 * levels[index - 1] + levels[index - 2]),
            Is.All.EqualTo(10));
        Assert.That(
            result.Content.Jobs.Select(job => job.Definition.ExperienceTable.Value),
            Is.All.EqualTo("experience.adventurer"),
            "every job takes the Adventurer's base table");
    }

    // Acquisition has no sight test (Gameplay Systems §10), so no walkable cell outside the chamber may come within the
    // boss spawn's radius, roam, and perception together: the boss never notices a player in the next hall.
    [Test]
    public void Run_ForRepositoryContent_KeepsEveryCellOutsideTheChamberBeyondTheSlimeMonarchsNotice()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition grotto = MapOf(result, Grotto);
        MonsterDefinition definition = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == Monarch)
            .Definition;
        MonsterSpawn spawn = grotto.MonsterSpawns.Single(candidate => candidate.Monster == definition.Id);
        double notice = spawn.Radius + definition.RoamRadius + definition.PerceptionRadius;
        NavigationGrid grid = grotto.Navigation;
        double half = grid.CellSize / 2d;

        var noticed = new List<string>();
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                WorldPosition center = grid.GetCellCenter(column, row);
                bool isInChamber = center.X - half >= ChamberMin
                    && center.X + half <= ChamberMax
                    && center.Z - half >= ChamberMin
                    && center.Z + half <= ChamberMax;
                // The cell's nearest point to the spawn's center.
                double dx = Math.Max(0d, Math.Abs(center.X - spawn.Center.X) - half);
                double dz = Math.Max(0d, Math.Abs(center.Z - spawn.Center.Z) - half);
                if (grid.GetCell(column, row).IsWalkable && !isInChamber && Math.Sqrt(dx * dx + dz * dz) <= notice)
                {
                    noticed.Add($"({center.X}, {center.Z})");
                }
            }
        }

        Assert.That(notice, Is.EqualTo(8d));
        Assert.That(noticed, Is.Empty);
    }

    [Test]
    public void Run_ForRepositoryContent_PlacesALinkingPackOfGrottoCrawlersInTheCrawlerHall()
    {
        ContentPipelineResult result = RepositoryContent();

        AuthoredMonster crawler = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == "monster.grotto_crawler");
        MonsterDefinition definition = crawler.Definition;
        Assert.That(
            (definition.Level, definition.Behavior, definition.Assists, definition.AssistRadius),
            Is.EqualTo((14, MonsterBehavior.Passive, true, 11d)));
        Assert.That(
            (crawler.Prefab, crawler.Scale, crawler.Tint),
            Is.EqualTo(("monster_forest_crawler", (double?)1.2, "#4B6B3C")));
        Assert.That(
            definition.Drops.Select(drop => (drop.Item.Value, drop.Chance)),
            Is.EqualTo(
                new[]
                {
                    ("item.material.grotto_carapace", 0.6), ("item.weapon.iron_sword", 0.05),
                    ("item.armor.leather", 0.05), ("item.consumable.minor_health", 0.25),
                    ("item.material.wardstone", 0.08), ("item.card.grotto_crawler", 0.001),
                    ("item.consumable.wind_converter", 0.03)
                }),
            "Wardstone, the card, then the converter, last, so every earlier draw keeps its place");
        MonsterSpawn spawn = MapOf(result, Grotto).MonsterSpawns
            .Single(candidate => candidate.Monster == definition.Id);
        Assert.That(spawn.Count, Is.EqualTo(6));
        Assert.That(spawn.Center.X - spawn.Radius, Is.GreaterThanOrEqualTo(-30d), "inside the crawler hall");
        Assert.That(spawn.Center.X + spawn.Radius, Is.LessThanOrEqualTo(-7d));
        Assert.That(spawn.Center.Z - spawn.Radius, Is.GreaterThanOrEqualTo(2d));
        Assert.That(spawn.Center.Z + spawn.Radius, Is.LessThanOrEqualTo(29d));
    }

    // Every place within either circle can hold a monster, and the barrow pack's roam reaches the entrance hall's
    // corridor band (z -22 to -18) that every walk through the grotto crosses.
    [Test]
    public void Run_ForRepositoryContent_PlacesTheHollowWispsInTheGallery_AndAPassiveBarrowPackInTheEntranceHall()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition grotto = MapOf(result, Grotto);
        AuthoredMonster wisp = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == "monster.hollow_wisp");
        AuthoredMonster crawler = result.Content.Monsters
            .Single(monster => monster.Definition.Id.Value == "monster.barrow_crawler");
        SkillDefinition bite = result.Content.Skills
            .Single(skill => skill.Definition.Id.Value == "skill.grave_bite")
            .Definition;

        Assert.That(
            new[] { wisp.Definition, crawler.Definition }
                .Select(monster => (monster.Level, monster.Hp, monster.PhysicalAttack, monster.PhysicalDefense,
                    monster.Behavior, monster.Assists, monster.BaseExperience, monster.JobExperience)),
            Is.EqualTo(
                new[]
                {
                    (17, 520, 32, 8, MonsterBehavior.Passive, false, 165, 120),
                    (15, 1000, 58, 20, MonsterBehavior.Passive, true, 135, 100)
                }));
        Assert.That(
            new[] { wisp, crawler }.Select(monster => (monster.Prefab, monster.Scale, monster.Tint)),
            Is.EqualTo(
                new[]
                {
                    ("monster_spark_wisp", 0.9, "#BFD8E6"),
                    ("monster_forest_crawler", (double?)1.1, "#CFC8B0")
                }));
        Assert.That(
            (wisp.Definition.AttackRange, wisp.Definition.KeepDistance,
                string.Join(" ", wisp.Definition.Skills.Select(entry => $"{entry.Skill.Value} {entry.Chance}"))),
            Is.EqualTo((6d, 3.5d, "skill.lull 0.15")));
        Assert.That(
            crawler.Definition.Skills.Select(entry => (entry.Skill.Value, entry.Chance)),
            Is.EqualTo(new[] { ("skill.grave_bite", 0.2) }));
        Assert.That(
            (bite.TargetType, bite.DamageType, bite.Element, bite.Range, bite.Levels.Single().CooldownMs,
                bite.Levels.Single().FixedCastMs),
            Is.EqualTo((SkillTargetType.Enemy, (SkillDamageType?)SkillDamageType.Physical,
                (SkillElement?)SkillElement.Shadow, 1.5d, 6000, 0)));
        Assert.That(
            new[] { wisp.Definition, crawler.Definition }
                .Select(monster => string.Join(" ", monster.Drops.Select(drop => $"{drop.Item.Value} {drop.Chance}"))),
            Is.EqualTo(
                new[]
                {
                    "item.material.gloom_dust 0.5 item.consumable.minor_mana 0.25 item.card.hollow_wisp 0.001 "
                    + "item.consumable.water_converter 0.03 item.consumable.grand_remedy 0.02",
                    "item.material.grotto_carapace 0.5 item.consumable.minor_health 0.25 item.card.barrow_crawler 0.001 "
                    + "item.consumable.earth_converter 0.03 item.consumable.grand_remedy 0.02"
                }));

        MonsterSpawn[] spawns = grotto.MonsterSpawns.Skip(3).ToArray();
        Assert.That(
            spawns.Select(spawn => (spawn.Monster.Value, spawn.Center.X, spawn.Center.Z, spawn.Radius, spawn.Count,
                spawn.RespawnMs)),
            Is.EqualTo(
                new[]
                {
                    ("monster.hollow_wisp", 24.5f, -26.5f, 3d, 4, 20_000),
                    ("monster.barrow_crawler", -20.5f, -26.5f, 3d, 4, 20_000)
                }),
            "after the boss's, so the earlier spawns keep their entity ids");
        NavigationGrid grid = grotto.Navigation;
        var blocked = new List<string>();
        foreach (MonsterSpawn spawn in spawns)
        {
            for (double dx = -spawn.Radius; dx <= spawn.Radius; dx += 0.5d)
            {
                for (double dz = -spawn.Radius; dz <= spawn.Radius; dz += 0.5d)
                {
                    float x = spawn.Center.X + (float)dx;
                    float z = spawn.Center.Z + (float)dz;
                    if (dx * dx + dz * dz <= spawn.Radius * spawn.Radius && !grid.CanOccupy(x, z))
                    {
                        blocked.Add($"({x}, {z})");
                    }
                }
            }
        }

        Assert.That(blocked, Is.Empty);
        Assert.That(
            spawns[1].Center.Z + spawns[1].Radius + crawler.Definition.RoamRadius,
            Is.GreaterThanOrEqualTo(-22d),
            "the pack's roam reaches the corridor band");
    }

    [Test]
    public void Run_ForRepositoryContent_PlacesTheSlimeMonarchAloneInItsChamber_BackAboutAnHourAfterItsDeath()
    {
        ContentPipelineResult result = RepositoryContent();

        AuthoredMonster monarch = result.Content.Monsters.Single(monster => monster.Definition.Id.Value == Monarch);
        MonsterDefinition definition = monarch.Definition;
        SkillDefinition slam = result.Content.Skills
            .Single(skill => skill.Definition.Id.Value == "skill.quake_slam")
            .Definition;
        MonsterSpawn spawn = MapOf(result, Grotto).MonsterSpawns
            .Single(candidate => candidate.Monster == definition.Id);
        Assert.That(
            (definition.Level, definition.IsBoss, definition.Behavior, definition.Hp),
            Is.EqualTo((25, true, MonsterBehavior.Aggressive, 40_000)));
        Assert.That(
            (monarch.Prefab, monarch.Scale, monarch.Tint),
            Is.EqualTo(("monster_training_slime", (double?)2.6, "#C9A227")));
        Assert.That(
            definition.Skills.Select(entry => (entry.Skill.Value, entry.Chance)),
            Is.EqualTo(new[] { ("skill.quake_slam", 0.25), ("skill.frost_breath", 0.15) }),
            "Frost Breath after Quake Slam, which keeps its place");
        Assert.That(
            definition.Drops.Select(drop => (drop.Item.Value, drop.Chance)),
            Is.EqualTo(
                new[]
                {
                    ("item.material.monarch_jelly", 1d), ("item.weapon.iron_sword", 0.25),
                    ("item.weapon.ash_staff", 0.25), ("item.armor.leather", 0.25),
                    ("item.material.emberstone", 1d), ("item.material.wardstone", 1d),
                    ("item.card.slime_monarch", 0.01), ("item.consumable.fire_converter", 1d),
                    ("item.consumable.grand_remedy", 1d)
                }),
            "both ores, the card, the converter, then the Grand Remedy, last, so every earlier draw keeps its place");
        Assert.That(
            (slam.TargetType, slam.DamageType, slam.AreaRadius, slam.Levels.Single().FixedCastMs),
            Is.EqualTo((SkillTargetType.Self, (SkillDamageType?)SkillDamageType.Magical, 4d, 1500)));
        Assert.That(
            (spawn.Center.X, spawn.Center.Z, spawn.Radius, spawn.Count),
            Is.EqualTo((18f, 18f, 0d, 1)));
        Assert.That((spawn.RespawnMs, spawn.RespawnVarianceMs), Is.EqualTo((3_600_000, 600_000)));
        Assert.That(definition.MvpExperience, Is.EqualTo(3000));
        Assert.That(
            definition.MvpDrops.Select(prize => (prize.Item.Value, prize.Chance, prize.Amount)),
            Is.EqualTo(new[] { ("item.armor.monarch_mantle", 0.3, 1), ("item.material.monarch_jelly", 1d, 5) }));
    }
}
}
