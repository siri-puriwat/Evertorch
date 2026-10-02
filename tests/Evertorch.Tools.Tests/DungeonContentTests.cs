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

    // The entrance hall in the south-west, the crawler hall, the wisp gallery, and the boss chamber; every room can be
    // walked to from the spawn point within the monsters' path budget.
    [TestCase(-18.5f, 15.5f, "the crawler hall")]
    [TestCase(16f, -19.5f, "the wisp gallery")]
    [TestCase(18f, 18f, "the boss chamber")]
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
                    ("item.armor.leather", 0.05), ("item.consumable.minor_health", 0.25)
                }));
        MonsterSpawn spawn = MapOf(result, Grotto).MonsterSpawns
            .Single(candidate => candidate.Monster == definition.Id);
        Assert.That(spawn.Count, Is.EqualTo(6));
        Assert.That(spawn.Center.X - spawn.Radius, Is.GreaterThanOrEqualTo(-30d), "inside the crawler hall");
        Assert.That(spawn.Center.X + spawn.Radius, Is.LessThanOrEqualTo(-7d));
        Assert.That(spawn.Center.Z - spawn.Radius, Is.GreaterThanOrEqualTo(2d));
        Assert.That(spawn.Center.Z + spawn.Radius, Is.LessThanOrEqualTo(29d));
    }
}
}
