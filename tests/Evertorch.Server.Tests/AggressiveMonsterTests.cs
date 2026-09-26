using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The forest crawler on the training field (Gameplay Systems §10; the monster AI research note's vectors): it takes a
///     live player within its perception unprovoked, the nearest and then the lowest entity ID, never while it walks
///     home, and chases at 4.8 m/s, which a walking player outpaces, until its leash ends the chase.
/// </summary>
[TestFixture]
public sealed class AggressiveMonsterTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
    private static readonly WorldPosition Home = new(8f, 0f, 12f);

    private static float Distance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static MapInstance MapOf(TestServer server, MapDefinitionId id)
    {
        server.World.TryGetMap(id, out MapInstance? map);
        return map!;
    }

    // The crawlers are still idle this soon after they spawned. The chosen one stands on its home; the others are
    // moved far off, so only the chosen one can perceive the players.
    private static (TestServer Server, MonsterEntity Crawler, ConnectionId[] Players) OnTheField(int players)
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, combatRandom: new SureHitRandom());
        var entered = new ConnectionId[players];
        for (int index = 0; index < players; index++)
        {
            entered[index] = server.EnterWorld(index + 1);
            server.PlayerOf(entered[index]).CurrentHealth = 1_000_000;
            server.PlayerOf(entered[index]).Position = MapOf(server, Ground).Definition.Portals.Single().Center;
        }

        server.Tick(2);
        foreach (ConnectionId player in entered)
        {
            Assert.That(server.SessionOf(player).Character!.Map.Definition.Id, Is.EqualTo(Field), "crossed");
            server.PlayerOf(player).Position = new WorldPosition(-21f, 0f, -20f);
        }

        MonsterEntity[] crawlers = MapOf(server, Field).Monsters
            .Where(monster => monster.Definition.Id.Value == "monster.forest_crawler")
            .ToArray();
        Assert.That(crawlers, Has.Length.EqualTo(3));
        for (int index = 1; index < crawlers.Length; index++)
        {
            crawlers[index].Position = new WorldPosition(20f, 0f, -20f);
        }

        crawlers[0].Position = Home;
        Assert.That(crawlers[0].Brain.State, Is.EqualTo(MonsterAiState.Idle));
        return (server, crawlers[0], entered);
    }

    [TestCase(5.9f, true)]
    [TestCase(6.0f, true)]
    [TestCase(6.1f, false)]
    public void Crawler_Idle_TakesALivePlayerWithinItsPerception_AtItsNextDecision(float distance, bool isTaken)
    {
        (TestServer server, MonsterEntity crawler, ConnectionId[] players) = OnTheField(1);
        PlayerEntity player = server.PlayerOf(players[0]);
        player.Position = new WorldPosition(Home.X + distance, 0f, Home.Z);

        server.Tick(3);

        Assert.That(crawler.Target, Is.EqualTo(isTaken ? player.Id : default));
        Assert.That(player.Target, Is.EqualTo(default(EntityId)), "the player did nothing to it");
    }

    [Test]
    public void Crawler_BesideADeadPlayer_TakesNobody()
    {
        (TestServer server, MonsterEntity crawler, ConnectionId[] players) = OnTheField(1);
        PlayerEntity player = server.PlayerOf(players[0]);
        player.Position = new WorldPosition(Home.X + 3f, 0f, Home.Z);
        server.Combat.Kill(MapOf(server, Field), player, null, server.CurrentTick);

        for (int tick = 0; tick < 6; tick++)
        {
            server.Tick();
            Assert.That(crawler.Target, Is.EqualTo(default(EntityId)), $"tick {tick}");
            Assert.That(crawler.Brain.State, Is.EqualTo(MonsterAiState.Idle), $"tick {tick}");
        }
    }

    [Test]
    public void Crawler_BetweenTwoPlayersAtTheSameDistance_TakesTheLowerEntityId()
    {
        (TestServer server, MonsterEntity crawler, ConnectionId[] players) = OnTheField(2);
        PlayerEntity first = server.PlayerOf(players[0]);
        PlayerEntity second = server.PlayerOf(players[1]);
        second.Position = new WorldPosition(Home.X + 4f, 0f, Home.Z);
        first.Position = new WorldPosition(Home.X - 4f, 0f, Home.Z);

        server.Tick(3);

        Assert.That(crawler.Target.Value, Is.EqualTo(Math.Min(first.Id.Value, second.Id.Value)));
    }

    [Test]
    public void Crawler_ChasingAWalkingPlayer_Moves4Point8MetresASecond_SoThePlayerGetsAway()
    {
        (TestServer server, MonsterEntity crawler, ConnectionId[] players) = OnTheField(1);
        PlayerEntity player = server.PlayerOf(players[0]);
        player.Position = new WorldPosition(Home.X - 5.5f, 0f, Home.Z);
        server.Tick(3);
        Assert.That(crawler.Target, Is.EqualTo(player.Id), "it went for the player");

        uint sequence = 1;

        void Walk(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                server.SendMove(players[0], sequence++, -1f, 0f, 1);
                server.Tick();
            }
        }

        Walk(10);
        float gapBefore = Distance(crawler.Position, player.Position);
        var steps = new List<float>();
        for (int tick = 0; tick < 40; tick++)
        {
            WorldPosition before = crawler.Position;
            Walk(1);
            steps.Add(Distance(before, crawler.Position));
        }

        float gapAfter = Distance(crawler.Position, player.Position);
        Assert.That(steps, Is.All.EqualTo(0.24f).Within(1e-4f), "4.8 m/s at 20 ticks a second");
        Assert.That(gapAfter - gapBefore, Is.EqualTo(0.4f).Within(0.01f), "the gap grows 0.2 m each second");
        Assert.That(crawler.Target, Is.EqualTo(player.Id), "still within its leash");
    }

    [Test]
    public void Crawler_LedPastItsLeash_WalksHome_TakingNobodyUntilIdle()
    {
        (TestServer server, MonsterEntity crawler, ConnectionId[] players) = OnTheField(1);
        PlayerEntity player = server.PlayerOf(players[0]);
        player.Position = new WorldPosition(Home.X - 5f, 0f, Home.Z);
        server.Tick(3);
        Assert.That(crawler.Target, Is.EqualTo(player.Id));

        player.Position = new WorldPosition(Home.X - 19f, 0f, Home.Z);
        float turnedAt = 0f;
        for (int tick = 0; tick < 200 && crawler.Brain.State != MonsterAiState.ReturnHome; tick++)
        {
            server.Tick();
            turnedAt = Distance(crawler.Position, crawler.Home);
        }

        Assert.That(crawler.Brain.State, Is.EqualTo(MonsterAiState.ReturnHome));
        Assert.That(turnedAt, Is.GreaterThan(14f), "the leash is 14 m from home");
        Assert.That(crawler.Target, Is.EqualTo(default(EntityId)));

        // The player keeps 3 m beside it all the way home: nothing is taken until the crawler is idle again.
        for (int tick = 0; tick < 200 && crawler.Brain.State == MonsterAiState.ReturnHome; tick++)
        {
            player.Position = new WorldPosition(crawler.Position.X, 0f, crawler.Position.Z - 3f);
            server.Tick();
            Assert.That(crawler.Target, Is.EqualTo(default(EntityId)), "nothing is taken on the way home");
        }

        Assert.That(crawler.Brain.State, Is.EqualTo(MonsterAiState.Idle));
        player.Position = new WorldPosition(crawler.Position.X, 0f, crawler.Position.Z - 3f);
        server.Tick(3);
        Assert.That(crawler.Target, Is.EqualTo(player.Id), "idle again, it takes the player beside it");
    }
}
}
