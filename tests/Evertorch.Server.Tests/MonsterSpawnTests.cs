using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class MonsterSpawnTests
{
    private const int Half = 500_000;

    // North row first, 1 m cells from the origin. The west room holds the spawn center; the east room lies inside
    // the radius but has no way in.
    private static readonly string[] SealedRooms =
    {
        "#########",
        "#...#...#",
        "#...#...#",
        "#...#...#",
        "#########"
    };

    private static readonly MonsterSpawn RoomSpawn = new(
        new MonsterDefinitionId("monster.a"),
        new WorldPosition(2.5f, 0f, 2.5f),
        5.0,
        1,
        8000);

    private static NavigationGrid CreateGrid(string[] northFirstRows)
    {
        int rows = northFirstRows.Length;
        int columns = northFirstRows[0].Length;
        var cells = new NavigationCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            string text = northFirstRows[rows - 1 - row];
            for (int column = 0; column < columns; column++)
            {
                NavigationSurface surface = text[column] == '#' ? NavigationSurface.Wall : NavigationSurface.Floor;
                cells[row * columns + column] = NavigationCell.Level(surface, 0f);
            }
        }

        return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }

    // A draw of n maps to an offset of radius × (2n / 10⁶ − 1).
    private static int DrawFor(double offset, double radius)
    {
        return (int)Math.Round((offset / radius + 1.0) / 2.0 * 1_000_000);
    }

    private static List<WorldPosition> MonsterPositions(TestServer server)
    {
        return server.World.Maps.Single().Monsters.Select(monster => monster.Position).ToList();
    }

    [Test]
    public void ChooseSpawnPoint_AfterSixteenUnsuitableDraws_UsesTheCenter()
    {
        var placement = new MonsterPlacement(CreateGrid(SealedRooms));
        var random = new ScriptedRandom(0);

        WorldPosition point = placement.ChooseSpawnPoint(RoomSpawn, random);

        Assert.That(point, Is.EqualTo(RoomSpawn.Center));
        Assert.That(random.Calls, Is.EqualTo(MonsterPlacement.MaxDraws * 2));
    }

    [Test]
    public void ChooseSpawnPoint_ForAPointInsideAWall_DrawsAgain()
    {
        var placement = new MonsterPlacement(CreateGrid(SealedRooms));
        var random = new ScriptedRandom(0, DrawFor(2.0, 5.0), Half, DrawFor(-1.0, 5.0), Half);

        WorldPosition point = placement.ChooseSpawnPoint(RoomSpawn, random);

        Assert.That(point, Is.EqualTo(new WorldPosition(1.5f, 0f, 2.5f)));
    }

    [Test]
    public void ChooseSpawnPoint_ForAStandablePointThatCannotBeReached_DrawsAgain()
    {
        var placement = new MonsterPlacement(CreateGrid(SealedRooms));
        var random = new ScriptedRandom(0, DrawFor(4.0, 5.0), Half, DrawFor(-1.0, 5.0), Half);

        WorldPosition point = placement.ChooseSpawnPoint(RoomSpawn, random);

        Assert.That(point, Is.EqualTo(new WorldPosition(1.5f, 0f, 2.5f)));
    }

    [Test]
    public void EnterWorld_NearTheSlimes_SpawnsEachSlimeAsAMonster()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId player = server.EnterWorld(1);

        server.Tick();

        EntitySpawn[] spawns = server.Transport.ControlSentTo(player)
            .Select(message => EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn != null)
            .Select(spawn => spawn!)
            .ToArray();
        Assert.That(spawns.Select(spawn => spawn.Kind), Is.All.EqualTo(EntityKind.Monster));
        Assert.That(spawns.Select(spawn => spawn.DefinitionId), Is.All.EqualTo("monster.training_slime"));
        Assert.That(spawns, Has.Length.EqualTo(4));
        EntitySnapshot snapshot = server.Transport.SnapshotsSentTo(player).Last();
        IEnumerable<EntityId> others = snapshot.Entities.Skip(1).Select(state => state.Entity);
        Assert.That(others, Is.EquivalentTo(spawns.Select(spawn => spawn.Entity)));
    }

    [Test]
    public void PlayerLeavingTheirArea_DespawnsTheSlimesAsOutOfRange()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick();
        server.Transport.ClearSent();

        server.Place(player, -20f, -20f);
        server.Tick();

        EntityDespawn[] despawns = server.Transport.ControlSentTo(player)
            .Select(message => EntityDespawn.TryRead(message.Payload, out EntityDespawn despawn)
                ? (EntityDespawn?)despawn
                : null)
            .Where(despawn => despawn.HasValue)
            .Select(despawn => despawn!.Value)
            .ToArray();
        Assert.That(despawns, Has.Length.EqualTo(4));
        Assert.That(despawns.Select(despawn => despawn.Reason), Is.All.EqualTo(DespawnReason.OutOfRange));
    }

    [Test]
    public void Status_ForTheTrainingGround_CountsItsMonsters()
    {
        var server = new TestServer(withMonsters: true);

        server.Tick();

        Assert.That(server.Status.Current.MonstersPerMap["map.training_ground"], Is.EqualTo(4));
    }

    [Test]
    public void World_ForTheTrainingGround_PlacesEachSlimeWhereItCanStandAndBeReached()
    {
        var server = new TestServer(withMonsters: true);
        MapInstance map = server.World.Maps.Single();
        MonsterSpawn spawn = map.Definition.MonsterSpawns.Single();
        NavigationGrid grid = map.Definition.Navigation;
        var pathfinder = new GridPathfinder(grid);
        var waypoints = new List<WorldPosition>();

        Assert.That(map.Monsters, Has.Count.EqualTo(spawn.Count));
        foreach (MonsterEntity monster in map.Monsters)
        {
            float dx = monster.Position.X - spawn.Center.X;
            float dz = monster.Position.Z - spawn.Center.Z;
            grid.TrySampleHeight(monster.Position.X, monster.Position.Z, out float ground);
            Assert.That(Math.Sqrt(dx * dx + dz * dz), Is.LessThanOrEqualTo(spawn.Radius));
            Assert.That(grid.CanOccupy(monster.Position.X, monster.Position.Z), Is.True);
            Assert.That(monster.Position.Y, Is.EqualTo(ground));
            Assert.That(pathfinder.TryFindPath(spawn.Center, monster.Position, 8192, waypoints), Is.True);
            Assert.That(monster.Home, Is.EqualTo(spawn.Center));
            Assert.That(monster.MovementSpeed, Is.EqualTo(4f));
        }
    }

    [Test]
    public void World_WithTheSameSeed_PlacesTheSlimesIdentically()
    {
        List<WorldPosition> first = MonsterPositions(new TestServer(withMonsters: true, randomSeed: 7));
        List<WorldPosition> again = MonsterPositions(new TestServer(withMonsters: true, randomSeed: 7));
        List<WorldPosition> other = MonsterPositions(new TestServer(withMonsters: true, randomSeed: 8));

        Assert.That(again, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }
}
}
