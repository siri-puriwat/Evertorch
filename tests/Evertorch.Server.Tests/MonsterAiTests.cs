using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class MonsterAiTests
{
    private static float Distance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static (TestServer Server, ConnectionId Player, MonsterEntity Slime) EnterBesideASlime()
    {
        var server = new TestServer(withMonsters: true, combatRandom: new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 1_000_000;
        StandBeside(server, entity, slime.Position, 1.2f);
        server.Tick();
        return (server, player, slime);
    }

    private static void StandBeside(TestServer server, PlayerEntity player, WorldPosition near, float distance)
    {
        NavigationGrid grid = server.World.Maps.Single().Definition.Navigation;
        for (int step = 0; step < 16; step++)
        {
            double angle = step * Math.PI / 8.0;
            float x = near.X + distance * (float)Math.Cos(angle);
            float z = near.Z + distance * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), near))
            {
                player.Position = new WorldPosition(x, height, z);
                return;
            }
        }

        throw new InvalidOperationException("No place to stand.");
    }

    private static void Provoke(TestServer server, ConnectionId player, MonsterEntity slime)
    {
        server.SendAttack(player, slime.Id, 1);
        for (int tick = 0; tick < 100 && slime.Target == default; tick++)
        {
            server.Tick();
        }

        server.SendCancel(player, 2);
        server.Tick();
        Assert.That(slime.Target, Is.EqualTo(server.PlayerOf(player).Id), "the slime turned on its attacker");
    }

    [Test]
    public void DeadMonster_StaysAsACorpseThenRespawnsAsANewEntity()
    {
        var server = new TestServer(withMonsters: true);
        MapInstance map = server.World.Maps.Single();
        MonsterEntity slime = map.Monsters.First();
        server.Tick();

        server.Combat.Kill(map, slime, null, server.CurrentTick);
        server.Tick(20);
        bool keptAsCorpse = map.Contains(slime.Id);
        server.Tick(2);
        bool removedAfterASecond = !map.Contains(slime.Id);
        int countWhileDead = map.Monsters.Count;
        server.Tick(8000 / 50);

        Assert.That(keptAsCorpse, Is.True, "a corpse stays for World:MonsterCorpseMs, 1000 ms");
        Assert.That(removedAfterASecond, Is.True);
        Assert.That(countWhileDead, Is.EqualTo(3));
        Assert.That(map.Monsters.Count, Is.EqualTo(4), "respawned 8000 ms after its death");
        MonsterEntity respawned = map.Monsters.Last();
        Assert.That(respawned.Id, Is.Not.EqualTo(slime.Id));
        Assert.That(respawned.CurrentHealth, Is.EqualTo(respawned.MaxHealth));
        Assert.That(respawned.Brain.State, Is.EqualTo(MonsterAiState.Idle));
    }

    [Test]
    public void Decisions_RunOnTheScanCadenceFromSpawn()
    {
        var server = new TestServer(withMonsters: true);
        MonsterEntity slime = server.World.Maps.Single().Monsters.First();

        server.Tick(11);

        Assert.That(slime.Definition.ScanIntervalMs, Is.EqualTo(100));
        Assert.That(slime.Brain.Decisions, Is.EqualTo(6), "0, 100, …, 500 ms: every second tick at 20 Hz");
    }

    [Test]
    public void Idle_ThenRoamsNearHome()
    {
        var server = new TestServer(withMonsters: true);
        MonsterEntity slime = server.World.Maps.Single().Monsters.First();
        WorldPosition start = slime.Position;
        float farthest = 0f;

        for (int tick = 0; tick < 400; tick++)
        {
            server.Tick();
            farthest = Math.Max(farthest, Distance(slime.Position, slime.Home));
        }

        Assert.That(slime.Position, Is.Not.EqualTo(start), "the idle pause is 4–5 s, then it roams");
        Assert.That(farthest, Is.LessThanOrEqualTo((float)slime.Definition.RoamRadius + 1f));
    }

    [Test]
    public void Leash_WhenTheChaseLeadsTooFarFromHome_ReturnsHomeAndDropsTheTarget()
    {
        (TestServer server, ConnectionId player, MonsterEntity slime) = EnterBesideASlime();
        Provoke(server, player, slime);
        NavigationGrid grid = server.World.Maps.Single().Definition.Navigation;
        var pathfinder = new GridPathfinder(grid);
        var waypoints = new List<WorldPosition>();
        WorldPosition? far = null;
        for (int row = 0; row < grid.Rows && far == null; row++)
        {
            for (int column = 0; column < grid.Columns && far == null; column++)
            {
                WorldPosition center = grid.GetCellCenter(column, row);
                float distance = Distance(center, slime.Home);
                if (distance > 16f
                    && distance < 20f
                    && grid.CanOccupy(center.X, center.Z)
                    && pathfinder.TryFindPath(slime.Position, center, 8192, waypoints))
                {
                    far = center;
                }
            }
        }

        server.PlayerOf(player).Position = far!.Value;
        bool hasLeashed = false;
        for (int tick = 0; tick < 400 && !hasLeashed; tick++)
        {
            server.Tick();
            hasLeashed = slime.Brain.State == MonsterAiState.ReturnHome;
        }

        Assert.That(hasLeashed, Is.True);
        Assert.That(slime.Target, Is.EqualTo(default(EntityId)));
        Assert.That(Distance(slime.Position, slime.Home), Is.GreaterThan((float)slime.Definition.LeashRadius));
        float arrivedAt = float.MaxValue;
        for (int tick = 0; tick < 300 && slime.Brain.State == MonsterAiState.ReturnHome; tick++)
        {
            server.Tick();
            Assert.That(slime.Target, Is.EqualTo(default(EntityId)), "it does not turn back on the way");
            arrivedAt = Distance(slime.Position, slime.Home);
        }

        Assert.That(slime.Brain.State, Is.EqualTo(MonsterAiState.Idle));
        Assert.That(arrivedAt, Is.LessThanOrEqualTo(MonsterAiSystem.HomeArrivalDistance));
        Assert.That(slime.CurrentHealth, Is.LessThan(slime.MaxHealth), "no healing on the way home");
    }

    [Test]
    public void PassiveSlime_IgnoresAPlayerThatDoesNotHitIt()
    {
        (TestServer server, ConnectionId player, MonsterEntity slime) = EnterBesideASlime();
        server.Transport.ClearSent();

        server.Tick(60);

        Assert.That(slime.Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.Transport.ControlOpcodesSentTo(player), Does.Not.Contain(MessageOpcode.AttackStarted));
    }

    [Test]
    public void PassiveSlime_TurnsOnItsAttackerAndStandsStillWhileSwinging()
    {
        (TestServer server, ConnectionId player, MonsterEntity slime) = EnterBesideASlime();
        Provoke(server, player, slime);
        server.Transport.ClearSent();

        var positionsWhileSwinging = new List<WorldPosition>();
        for (int tick = 0; tick < 60; tick++)
        {
            server.Tick();
            if (slime.Combat.IsSwinging)
            {
                positionsWhileSwinging.Add(slime.Position);
            }
        }

        var starts = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.AttackStarted)
            .Select(message =>
            {
                AttackStarted.TryRead(message.Payload, out AttackStarted started);
                return started;
            })
            .Where(started => started.Attacker == slime.Id)
            .ToList();
        Assert.That(starts, Is.Not.Empty, "the slime attacks back");
        Assert.That(positionsWhileSwinging.Distinct().Count(), Is.EqualTo(1));
    }

    [Test]
    public void PassiveSlime_WhenItsTargetMovesAway_ChasesIt()
    {
        (TestServer server, ConnectionId player, MonsterEntity slime) = EnterBesideASlime();
        Provoke(server, player, slime);
        PlayerEntity entity = server.PlayerOf(player);
        StandBeside(server, entity, slime.Position, 4f);
        float before = Distance(slime.Position, entity.Position);

        server.Tick(30);

        Assert.That(Distance(slime.Position, entity.Position), Is.LessThan(before));
        Assert.That(Distance(slime.Position, entity.Position), Is.LessThanOrEqualTo(slime.AttackRange));
    }

    [Test]
    public void Target_WhenItDies_IsDroppedAndNotTakenBack()
    {
        (TestServer server, ConnectionId player, MonsterEntity slime) = EnterBesideASlime();
        Provoke(server, player, slime);
        MapInstance map = server.World.Maps.Single();

        server.Combat.Kill(map, server.PlayerOf(player), slime, server.CurrentTick);
        server.Tick(60);

        Assert.That(slime.Target, Is.EqualTo(default(EntityId)));
        Assert.That(slime.Combat.IsAutoAttacking, Is.False);
    }
}
}
