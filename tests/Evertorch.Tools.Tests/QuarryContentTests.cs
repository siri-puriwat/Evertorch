using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
/// <summary>
///     The Ashen Quarry as content (Gameplay Systems §2.2, §4.2, §9, §10, §11.7; Prototype Content §6): the open quarry
///     beyond the training field's northern gate, its four monsters, their five skills and their spawns, the boss alone
///     in its arena, the Gate Warden's two quarry quests, and the four cards.
/// </summary>
[TestFixture]
public sealed class QuarryContentTests
{
    private const string Quarry = "map.ashen_quarry";
    private const string Field = "map.training_field";
    private const string Cinder = "monster.cinder_crawler";
    private const string Basalt = "monster.basalt_crawler";
    private const string Wisp = "monster.ash_wisp";
    private const string Furnace = "monster.furnace_heart";

    // The furnace arena, from -12 m to 12 m east and from 16 m to 38 m north; its one way in is the north road.
    private const float ArenaWest = -12f;
    private const float ArenaEast = 12f;
    private const float ArenaSouth = 16f;
    private const float ArenaNorth = 38f;

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

    private static MonsterDefinition MonsterOf(ContentPipelineResult result, string id)
    {
        return result.Content.Monsters.Single(monster => monster.Definition.Id.Value == id).Definition;
    }

    // The x of each gate cell along the row of cells at z, west to east.
    private static float[] GatesAlong(NavigationGrid grid, float z)
    {
        var gates = new List<float>();
        for (int column = 0; column < grid.Columns; column++)
        {
            float x = grid.GetCellCenter(column, 0).X;
            if (grid.TryGetCellIndex(x, z, out int found, out int row)
                && grid.GetCell(found, row).Surface == NavigationSurface.Gate)
            {
                gates.Add(x);
            }
        }

        return gates.ToArray();
    }

    // The arrival yard in the south, the crossroads, the cinder basin in the west, the basalt galleries' three alcoves
    // in the east, the ash terraces in the north-west and north-east, and the furnace arena between them.
    [TestCase(0f, -38.5f, "the portal before the south gate")]
    [TestCase(0f, -5f, "the crossroads")]
    [TestCase(-28f, -10f, "the cinder basin")]
    [TestCase(32f, -27f, "the basalt galleries' southern alcove")]
    [TestCase(32f, -6f, "their middle alcove")]
    [TestCase(32f, 12f, "their northern alcove")]
    [TestCase(-27f, 28f, "the western ash terrace")]
    [TestCase(27f, 28f, "the eastern ash terrace")]
    [TestCase(0f, 28f, "the furnace arena")]
    public void Run_ForRepositoryContent_LetsEveryPartOfTheQuarryBeWalkedToFromItsSpawnPoint(
        float x,
        float z,
        string part)
    {
        MapDefinition quarry = MapOf(RepositoryContent(), Quarry);
        NavigationGrid grid = quarry.Navigation;
        var waypoints = new List<WorldPosition>();

        bool isReachable = new GridPathfinder(grid).TryFindPath(
            quarry.SpawnPosition,
            new WorldPosition(x, 0f, z),
            grid.Columns * grid.Rows,
            waypoints);

        Assert.That((grid.Columns, grid.Rows, grid.CellSize), Is.EqualTo((80, 80, 1f)));
        Assert.That(grid.Columns * grid.Rows, Is.LessThanOrEqualTo(PathNodeBudget));
        Assert.That(isReachable, Is.True, part);
    }

    private static AuthoredMonster AuthoredOf(ContentPipelineResult result, string id)
    {
        return result.Content.Monsters.Single(monster => monster.Definition.Id.Value == id);
    }

    private static SkillDefinition SkillOf(ContentPipelineResult result, string id)
    {
        return result.Content.Skills.Single(skill => skill.Definition.Id.Value == id).Definition;
    }

    private static string DropsOf(MonsterDefinition monster)
    {
        return string.Join(
            " ",
            monster.Drops.Select(drop => $"{drop.Item.Value} {drop.Chance} {drop.MinAmount}-{drop.MaxAmount}"));
    }

    private static double Squared(WorldPosition a, WorldPosition b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    // Owner decision 23: four cards, one for each monster, each sold on the board alone and taken out at the Smith.
    [Test]
    public void Run_ForRepositoryContent_GivesEachQuarryMonsterItsCard()
    {
        ContentPipelineResult result = RepositoryContent();
        AuthoredItem[] cards = new[] { "cinder_crawler", "basalt_crawler", "ash_wisp", "furnace_heart" }
            .Select(name => result.Content.Items.Single(item => item.Definition.Id.Value == $"item.card.{name}"))
            .ToArray();

        Assert.That(
            cards.Select(card => (card.Definition.DisplayName, card.Definition.Card!.Fits, card.Prefix,
                card.Description)),
            Is.EqualTo(
                new[]
                {
                    ("Cinder Crawler Card", ItemType.Garment, "Fireward", "Damage from Fire -20%."),
                    ("Basalt Crawler Card", ItemType.Shield, "Steady", "Stun resistance +20%."),
                    ("Ash Wisp Card", ItemType.Weapon, "Dousing", "Damage to Fire +20%."),
                    ("Furnace Heart Card", ItemType.Armor, "Molten", "Fire armor.")
                }));
        Assert.That(
            cards.Select(card => (card.Definition.Card!.RemovalFee, card.Definition.SellPrice,
                card.Definition.Market!.BasePrice, card.Definition.Market.Floor)),
            Is.EqualTo(new[]
                { (600, 0, 2400, 240), (600, 0, 2400, 240), (600, 0, 2400, 240), (5000, 0, 24_000, 2400) }));
        CardBonus[] bonuses = cards.Select(card => card.Definition.Card!.Bonus).ToArray();
        Assert.That(bonuses[0].ReductionFrom(Element.Fire), Is.EqualTo(20));
        Assert.That(bonuses[1].ResistanceTo(Ailment.Stun), Is.EqualTo(20));
        Assert.That(bonuses[2].DamageTo(Element.Fire), Is.EqualTo(20));
        Assert.That(bonuses[3].ArmorElement, Is.EqualTo(Element.Fire));
    }

    // Owner decision 22: the Crawler Hunt's rates, 1.2 times the kills' base experience, as much job experience, and
    // 0.8 coins a point; the boss's on one member's share of a party of three.
    [Test]
    public void Run_ForRepositoryContent_GivesTheQuarryHuntAndTheFurnaceHeartAtTheGateWarden()
    {
        ContentPipelineResult result = RepositoryContent();
        QuestDefinition hunt = result.Content.Quests.Single(quest => quest.Definition.Id.Value == "quest.quarry_hunt")
            .Definition;
        QuestDefinition boss = result.Content.Quests
            .Single(quest => quest.Definition.Id.Value == "quest.furnace_heart")
            .Definition;
        int killed = hunt.Count * MonsterOf(result, hunt.Monster.Value).BaseExperience;
        int share = MonsterOf(result, Furnace).BaseExperience / 3;

        Assert.That(
            (hunt.DisplayName, hunt.Giver.Value, hunt.Monster.Value, hunt.Count),
            Is.EqualTo(("Quarry Hunt", "npc.gate_warden", Cinder, 10)));
        Assert.That(
            (boss.DisplayName, boss.Giver.Value, boss.Monster.Value, boss.Count),
            Is.EqualTo(("The Furnace Heart", "npc.gate_warden", Furnace, 1)));
        Assert.That(
            (hunt.BaseExperience, hunt.JobExperience, hunt.Currency),
            Is.EqualTo((killed * 6 / 5, killed * 6 / 5, killed * 4 / 5)),
            "2,400, 2,400, and 1,600 for ten crawlers' 2,000");
        Assert.That(
            (boss.BaseExperience, boss.JobExperience, boss.Currency),
            Is.EqualTo((share * 6 / 5, share * 6 / 5, share * 4 / 5)),
            "3,000, 3,000, and 2,000 for a third of the boss's 7,500");
    }

    // Owner decision 21: five new skills, each a key and no art.
    [Test]
    public void Run_ForRepositoryContent_GivesTheQuarryItsFiveSkills()
    {
        ContentPipelineResult result = RepositoryContent();
        SkillDefinition bite = SkillOf(result, "skill.cinder_bite");
        SkillDefinition bash = SkillOf(result, "skill.basalt_bash");
        SkillDefinition gust = SkillOf(result, "skill.ash_gust");
        SkillDefinition burst = SkillOf(result, "skill.furnace_burst");
        SkillDefinition cloud = SkillOf(result, "skill.ash_cloud");

        Assert.That(
            new[] { bite, bash, gust }.Select(skill => (skill.TargetType, skill.DamageType, skill.Element, skill.Range,
                skill.Levels.Single().FixedCastMs, skill.Levels.Single().CooldownMs)),
            Is.EqualTo(
                new[]
                {
                    (SkillTargetType.Enemy, (SkillDamageType?)SkillDamageType.Physical,
                        (SkillElement?)SkillElement.Fire,
                        1.5d, 0, 8000),
                    (SkillTargetType.Enemy, SkillDamageType.Physical, SkillElement.Earth, 1.5d, 0, 10_000),
                    (SkillTargetType.Enemy, SkillDamageType.Magical, SkillElement.Wind, 6d, 600, 10_000)
                }));
        Assert.That(
            new[] { burst, cloud }.Select(skill => (skill.TargetType, skill.DamageType, skill.Element, skill.AreaRadius,
                skill.Levels.Single().FixedCastMs, skill.Levels.Single().AfterCastDelayMs,
                skill.Levels.Single().CooldownMs)),
            Is.EqualTo(
                new[]
                {
                    (SkillTargetType.Self, (SkillDamageType?)SkillDamageType.Magical, (SkillElement?)SkillElement.Fire,
                        5d, 2000, 1000, 12_000),
                    (SkillTargetType.Self, null, null, 6d, 2000, 1000, 18_000)
                }));
    }

    // Owner decision 19: the four monsters' numbers, how each fights and keeps its ground, its experience, and its body.
    [Test]
    public void Run_ForRepositoryContent_GivesTheQuarryItsFourMonsters()
    {
        ContentPipelineResult result = RepositoryContent();
        AuthoredMonster[] monsters = new[] { Cinder, Basalt, Wisp, Furnace }
            .Select(id => AuthoredOf(result, id))
            .ToArray();
        MonsterDefinition[] definitions = monsters.Select(monster => monster.Definition).ToArray();

        Assert.That(
            definitions.Select(monster => (monster.Level, monster.IsBoss, monster.Element, monster.Race, monster.Size)),
            Is.EqualTo(
                new[]
                {
                    (20, false, Element.Fire, Race.Insect, BodySize.Medium),
                    (22, false, Element.Earth, Race.Insect, BodySize.Large),
                    (23, false, Element.Wind, Race.Formless, BodySize.Small),
                    (25, true, Element.Fire, Race.Formless, BodySize.Large)
                }));
        Assert.That(
            definitions.Select(monster => (monster.Hp, monster.PhysicalAttack, monster.PhysicalDefense, monster.Hit,
                monster.Flee, monster.MagicAttack)),
            Is.EqualTo(
                new[]
                {
                    (1000, 48, 20, 240, 178, 0), (900, 44, 28, 245, 172, 0), (650, 34, 10, 250, 192, 80),
                    (42_000, 70, 40, 300, 165, 140)
                }));
        Assert.That(
            definitions.Select(monster => (monster.AttackRange, monster.AttackIntervalMs, monster.BaseSpeed,
                monster.KeepDistance)),
            Is.EqualTo(new[] { (1.5, 1600, 4.8, 0d), (1.5, 2000, 4.4, 0d), (6d, 1800, 4.4, 3.5), (2d, 2000, 4d, 0d) }));
        Assert.That(
            definitions.Select(monster => (monster.Behavior, monster.PerceptionRadius, monster.LeashRadius,
                monster.RoamRadius, monster.Assists, monster.AssistRadius)),
            Is.EqualTo(
                new[]
                {
                    (MonsterBehavior.Aggressive, 5d, 12d, 3d, false, 0d),
                    (MonsterBehavior.Passive, 6d, 12d, 2d, true, 6d),
                    (MonsterBehavior.Passive, 6d, 14d, 4d, false, 0d),
                    (MonsterBehavior.Aggressive, 6d, 18d, 2d, false, 0d)
                }));
        Assert.That(
            definitions.Select(monster =>
                string.Join(" ", monster.Skills.Select(entry => $"{entry.Skill.Value} {entry.Chance}"))),
            Is.EqualTo(
                new[]
                {
                    "skill.cinder_bite 0.15", "skill.basalt_bash 0.15", "skill.ash_gust 0.15 skill.hush 0.15",
                    "skill.furnace_burst 0.25 skill.ash_cloud 0.2"
                }));
        Assert.That(
            definitions.Select(monster => (monster.BaseExperience, monster.JobExperience, monster.MvpExperience)),
            Is.EqualTo(new[] { (200, 150, 0), (240, 180, 0), (260, 195, 0), (7500, 3750, 3750) }));
        Assert.That(
            monsters.Select(monster => (monster.Prefab, monster.Scale, monster.Tint)),
            Is.EqualTo(
                new[]
                {
                    ("monster_forest_crawler", 1.15, "#9C3D1F"),
                    ("monster_forest_crawler", 1.35, "#4F555C"),
                    ("monster_spark_wisp", 1.0, "#D2CABD"),
                    ("monster_training_slime", (double?)2.8, "#C4501C")
                }));
    }

    // Owner decision 19: each drops what the dungeon's monsters drop, an ore at a tenth, and its card last; the boss its
    // ores, converters, and Grand Remedies whole, its card at 1 %, and ten Emberstones to its most valuable player.
    [Test]
    public void Run_ForRepositoryContent_GivesTheQuarrysMonstersTheirDrops_EachCardLast()
    {
        ContentPipelineResult result = RepositoryContent();
        MonsterDefinition furnace = MonsterOf(result, Furnace);

        Assert.That(
            new[] { Cinder, Basalt, Wisp, Furnace }.Select(id => DropsOf(MonsterOf(result, id))),
            Is.EqualTo(
                new[]
                {
                    "item.material.grotto_carapace 0.6 1-2 item.consumable.minor_health 0.25 1-2 "
                    + "item.material.emberstone 0.1 1-1 item.consumable.water_converter 0.03 1-1 "
                    + "item.card.cinder_crawler 0.001 1-1",
                    "item.material.grotto_carapace 0.6 1-2 item.consumable.minor_health 0.25 1-2 "
                    + "item.material.wardstone 0.1 1-1 item.consumable.fire_converter 0.03 1-1 "
                    + "item.card.basalt_crawler 0.001 1-1",
                    "item.material.gloom_dust 0.6 1-2 item.consumable.minor_mana 0.25 1-2 "
                    + "item.material.emberstone 0.1 1-1 item.consumable.earth_converter 0.03 1-1 "
                    + "item.consumable.grand_remedy 0.02 1-1 item.card.ash_wisp 0.001 1-1",
                    "item.material.emberstone 1 5-10 item.material.wardstone 1 5-10 "
                    + "item.consumable.water_converter 1 1-3 item.consumable.grand_remedy 1 2-4 "
                    + "item.card.furnace_heart 0.01 1-1"
                }));
        Assert.That(
            furnace.MvpDrops.Select(prize => (prize.Item.Value, prize.Chance, prize.Amount)),
            Is.EqualTo(new[] { ("item.material.emberstone", 1d, 10) }));
    }

    // Owner decision 20: two gate cells in the middle of the field's north wall and two in the quarry's south wall,
    // each with its portal on the floor before it; an arrival stands two metres or one and a half inside the gate,
    // facing away from it.
    [Test]
    public void Run_ForRepositoryContent_JoinsTheFieldsNorthGateToTheQuarrysSouthGate()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition field = MapOf(result, Field);
        MapDefinition quarry = MapOf(result, Quarry);
        MapPortal north = field.Portals.Single(portal => portal.DestinationMap == quarry.Id);
        MapPortal south = quarry.Portals.Single();

        Assert.That(GatesAlong(field.Navigation, 23.5f), Is.EqualTo(new[] { -12.5f, -11.5f }),
            "the field's north gate");
        Assert.That(GatesAlong(quarry.Navigation, -39.5f), Is.EqualTo(new[] { -0.5f, 0.5f }),
            "the quarry's south gate");
        Assert.That(
            (north.Center.X, north.Center.Z, north.Radius, north.DestinationPosition.X, north.DestinationPosition.Z,
                north.DestinationFacing.Z),
            Is.EqualTo((-12f, 22.5f, 1d, 0f, -37f, 1f)));
        Assert.That(
            (south.DestinationMap.Value, south.Center.X, south.Center.Z, south.Radius, south.DestinationPosition.X,
                south.DestinationPosition.Z, south.DestinationFacing.Z),
            Is.EqualTo((Field, 0f, -38.5f, 1d, -12f, 20.5f, -1f)));
        Assert.That(quarry.SpawnPosition, Is.EqualTo(north.DestinationPosition), "arrivals in the yard");
        Assert.That(
            field.Portals.Select(portal => portal.DestinationMap.Value),
            Is.EqualTo(new[] { "map.training_ground", "map.umbral_grotto", Quarry }),
            "after the field's two portals, which keep their places");
    }

    // A Basalt pair's caller, anywhere within its leash, never stands within twice the assist radius in sight of
    // another pair's spawn, so a fight in one alcove never calls another's pair (owner decision 20).
    [Test]
    public void Run_ForRepositoryContent_KeepsEachBasaltPairsCallsInItsAlcove()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition quarry = MapOf(result, Quarry);
        MonsterDefinition basalt = MonsterOf(result, Basalt);
        NavigationGrid grid = quarry.Navigation;
        MonsterSpawn[] pairs = quarry.MonsterSpawns.Where(spawn => spawn.Monster == basalt.Id).ToArray();

        var heard = new List<string>();
        foreach (MonsterSpawn caller in pairs)
        {
            for (int row = 0; row < grid.Rows; row++)
            {
                for (int column = 0; column < grid.Columns; column++)
                {
                    WorldPosition center = grid.GetCellCenter(column, row);
                    if (!grid.GetCell(column, row).IsWalkable
                        || Math.Sqrt(Squared(center, caller.Center)) > basalt.LeashRadius)
                    {
                        continue;
                    }

                    foreach (MonsterSpawn other in pairs.Where(pair => pair != caller))
                    {
                        if (Math.Sqrt(Squared(center, other.Center)) <= 2 * basalt.AssistRadius + other.Radius
                            && grid.HasLineOfSight(center, other.Center))
                        {
                            heard.Add($"({center.X}, {center.Z}) calls ({other.Center.X}, {other.Center.Z})");
                        }
                    }
                }
            }
        }

        Assert.That(heard, Is.Empty);
    }

    // Acquisition has no sight test (Gameplay Systems §10), so no walkable cell outside the arena may come within the
    // boss spawn's radius, roam, and perception together: the boss never notices a player on the terraces or the road
    // below.
    [Test]
    public void Run_ForRepositoryContent_KeepsEveryCellOutsideTheArenaBeyondTheFurnaceHeartsNotice()
    {
        ContentPipelineResult result = RepositoryContent();
        MapDefinition quarry = MapOf(result, Quarry);
        MonsterDefinition boss = MonsterOf(result, Furnace);
        MonsterSpawn spawn = quarry.MonsterSpawns.Single(candidate => candidate.Monster == boss.Id);
        double notice = spawn.Radius + boss.RoamRadius + boss.PerceptionRadius;
        NavigationGrid grid = quarry.Navigation;
        double half = grid.CellSize / 2d;

        var noticed = new List<string>();
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                WorldPosition center = grid.GetCellCenter(column, row);
                bool isInArena = center.X - half >= ArenaWest
                    && center.X + half <= ArenaEast
                    && center.Z - half >= ArenaSouth
                    && center.Z + half <= ArenaNorth;
                double dx = Math.Max(0d, Math.Abs(center.X - spawn.Center.X) - half);
                double dz = Math.Max(0d, Math.Abs(center.Z - spawn.Center.Z) - half);
                if (grid.GetCell(column, row).IsWalkable && !isInArena && Math.Sqrt(dx * dx + dz * dz) <= notice)
                {
                    noticed.Add($"({center.X}, {center.Z})");
                }
            }
        }

        Assert.That(notice, Is.EqualTo(8d));
        Assert.That(noticed, Is.Empty);
        Assert.That(
            Math.Sqrt(Squared(spawn.Center, new WorldPosition(ArenaWest, 0f, ArenaSouth))),
            Is.LessThan(boss.LeashRadius),
            "the leash reaches past the arena's farthest corner");
    }

    // Owner decision 20: five Cinder Crawlers alone, about 11.7 m apart at the least; three Basalt pairs, one to an
    // alcove, out of each other's sight; two groups of three Ash Wisps; and the boss alone in its arena, back about an
    // hour after its death. Every place within each spawn's circle holds a monster and is reachable from the spawn
    // point.
    [Test]
    public void Run_ForRepositoryContent_SpawnsTheQuarrysMonstersWhereTheyCanStandAndBeReached()
    {
        MapDefinition quarry = MapOf(RepositoryContent(), Quarry);
        NavigationGrid grid = quarry.Navigation;
        MonsterSpawn[] spawns = quarry.MonsterSpawns.ToArray();
        MonsterSpawn[] cinders = spawns.Where(spawn => spawn.Monster.Value == Cinder).ToArray();
        MonsterSpawn[] pairs = spawns.Where(spawn => spawn.Monster.Value == Basalt).ToArray();
        var pathfinder = new GridPathfinder(grid);
        var waypoints = new List<WorldPosition>();
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
                        blocked.Add($"{spawn.Monster.Value} ({x}, {z})");
                    }
                }
            }

            if (!pathfinder.TryFindPath(quarry.SpawnPosition, spawn.Center, grid.Columns * grid.Rows, waypoints))
            {
                blocked.Add($"{spawn.Monster.Value} unreachable at ({spawn.Center.X}, {spawn.Center.Z})");
            }
        }

        Assert.That(
            spawns.Select(spawn => (spawn.Monster.Value, spawn.Count, spawn.Radius, spawn.RespawnMs,
                spawn.RespawnVarianceMs)),
            Is.EqualTo(
                Enumerable.Repeat((Cinder, 1, 2d, 20_000, 0), 5)
                    .Concat(Enumerable.Repeat((Basalt, 2, 1.5d, 20_000, 0), 3))
                    .Concat(Enumerable.Repeat((Wisp, 3, 4d, 20_000, 0), 2))
                    .Append((Furnace, 1, 0d, 3_600_000, 600_000))));
        Assert.That(blocked, Is.Empty);
        Assert.That(
            cinders.SelectMany((first, index) => cinders.Skip(index + 1)
                .Select(second => Math.Sqrt(Squared(first.Center, second.Center)))).Min(),
            Is.EqualTo(Math.Sqrt(136d)).Within(1e-9),
            "a hunter meets them one at a time: the nearest two 11.66 m apart, decision 20's 11.7 m");
        Assert.That(
            pairs.SelectMany((first, index) => pairs.Skip(index + 1)
                .Select(second => grid.HasLineOfSight(first.Center, second.Center))),
            Is.All.False,
            "no pair sees another");
    }
}
}
