using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Slime Monarch on the server (Gameplay Systems §9, §10; owner decisions 3, 6, and 14): Quake Slam strikes
///     every player within 4 m of the boss and in its sight when the cast ends, a death's respawn moves by up to its
///     spread either way, and once home from its leash the boss recovers all its HP and forgets who hurt it.
/// </summary>
[TestFixture]
public sealed class BossTests
{
    private const int Spare = 1_000_000;

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    private static MonsterEntity MonarchOf(TestServer server)
    {
        return GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch);
    }

    // The players enter the chamber west of the boss, at the given distances from it, each with HP to spare; with the
    // AI off only the test casts.
    private static (TestServer Server, ConnectionId[] Players, MonsterEntity Monarch) InTheChamber(
        params float[] distances)
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
        MonsterEntity monarch = MonarchOf(server);
        var players = new ConnectionId[distances.Length];
        for (int index = 0; index < distances.Length; index++)
        {
            players[index] = server.EnterWorld(index + 1);
            server.CrossIntoTheGrotto(players[index]);
            PlayerEntity player = server.PlayerOf(players[index]);
            player.Position = new WorldPosition(monarch.Position.X - distances[index], 0f, monarch.Position.Z);
            player.CurrentHealth = Spare;
        }

        server.Tick();
        return (server, players, monarch);
    }

    private static void BeginSlam(TestServer server, MonsterEntity monarch, PlayerEntity target)
    {
        MapInstance map = GrottoOf(server);
        Assert.That(server.Combat.CanMonsterCast(map, monarch, QuakeSlam, target, server.CurrentTick), Is.True);
        server.Combat.BeginMonsterCast(map, monarch, QuakeSlam, target, server.CurrentTick);
    }

    // The 1,500 ms cast ends within the ticks of the next 1.5 s and one more.
    private static void LetTheSlamLand(TestServer server)
    {
        server.Tick(TestServer.TickRate * 3 / 2 + 1);
    }

    private static List<SkillResolved> SlamsHeardBy(TestServer server, ConnectionId player)
    {
        var heard = new List<SkillResolved>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.SkillResolved
                && SkillResolved.TryRead(message.Payload, out SkillResolved? resolved)
                && resolved!.Skill == QuakeSlam)
            {
                heard.Add(resolved);
            }
        }

        return heard;
    }

    // The first pillar inside the chamber: a cell no one may walk on, with floor around it.
    private static WorldPosition PillarIn(MapInstance map)
    {
        NavigationGrid grid = map.Definition.Navigation;
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                WorldPosition center = grid.GetCellCenter(column, row);
                if (!grid.GetCell(column, row).IsWalkable
                    && center.X > 8f
                    && center.X < 28f
                    && center.Z > 8f
                    && center.Z < 28f)
                {
                    return center;
                }
            }
        }

        throw new InvalidOperationException("The chamber has no pillar.");
    }

    [TestCase(3.9f, true)]
    [TestCase(4.1f, false)]
    public void CanMonsterCast_QuakeSlam_ReachesAPlayerWithinItsAreaAlone(float distance, bool canCast)
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = InTheChamber(distance);

        Assert.That(
            server.Combat.CanMonsterCast(
                GrottoOf(server),
                monarch,
                QuakeSlam,
                server.PlayerOf(players[0]),
                server.CurrentTick),
            Is.EqualTo(canCast));
    }

    [Test]
    public void Monarch_FightingAPlayerWithinFourMetres_CastsQuakeSlamAroundItself()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true);
        ConnectionId connection = server.EnterWorld(1);
        server.CrossIntoTheGrotto(connection);
        PlayerEntity player = server.PlayerOf(connection);
        MonsterEntity monarch = MonarchOf(server);
        player.CurrentHealth = Spare;
        player.Position = new WorldPosition(monarch.Home.X - 3f, 0f, monarch.Home.Z);
        server.Transport.ClearSent();

        server.Tick(5 * TestServer.TickRate);

        SkillCastStarted? cast = server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.SkillCastStarted)
            .Select(message =>
                SkillCastStarted.TryRead(message.Payload, out SkillCastStarted? started) ? started : null)
            .FirstOrDefault(started => started != null && started.Skill == QuakeSlam);
        Assert.That(cast, Is.Not.Null, "the boss slammed within 5 s of noticing the player");
        Assert.That((cast!.Caster, cast.Target), Is.EqualTo((monarch.Id, default(EntityId))), "cast on itself");
        Assert.That(SlamsHeardBy(server, connection).Select(resolved => resolved.Target), Has.Member(player.Id));
    }

    [Test]
    public void Monarch_HomeFromItsLeash_RecoversAllItsHp_AndForgetsWhoHurtIt()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true);
        ConnectionId connection = server.EnterWorld(1);
        server.CrossIntoTheGrotto(connection);
        PlayerEntity player = server.PlayerOf(connection);
        MonsterEntity monarch = MonarchOf(server);
        player.CurrentHealth = Spare;
        player.Position = new WorldPosition(monarch.Home.X - 5f, 0f, monarch.Home.Z);
        monarch.CurrentHealth -= 5000;
        monarch.LogDamage(player.Character, 5000);
        monarch.LogMvpDealt(player.Character, 5000);
        server.Tick(TestServer.TickRate);
        bool wasNoticed = monarch.Target == player.Id;

        // Down the corridor toward the crawler hall, 22 m from its home: the chase passes its 16 m leash.
        player.Position = new WorldPosition(-4f, 0f, monarch.Home.Z);
        bool hasLeashed = false;
        bool isHome = false;
        for (int tick = 0; tick < 60 * TestServer.TickRate && !isHome; tick++)
        {
            server.Tick();
            hasLeashed |= monarch.Brain.State == MonsterAiState.ReturnHome;
            isHome = hasLeashed && monarch.Brain.State == MonsterAiState.Idle;
        }

        Assert.That(wasNoticed, Is.True, "the aggressive boss took the player within its 6 m");
        Assert.That((hasLeashed, isHome), Is.EqualTo((true, true)), "it walked home from its leash");
        Assert.That(monarch.CurrentHealth, Is.EqualTo(monarch.MaxHealth));
        Assert.That((monarch.DamageLog.Count, monarch.MvpLog.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public void Monarch_SpawnsAloneAtTheHeartOfItsChamberAtStartUp()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);

        MonsterEntity[] bosses = server.World.Maps
            .SelectMany(map => map.Monsters)
            .Where(monster => monster.Definition.IsBoss)
            .ToArray();
        Assert.That(bosses.Select(boss => boss.Definition.Id), Is.EqualTo(new[] { Monarch }));
        Assert.That((bosses[0].Position.X, bosses[0].Position.Z), Is.EqualTo((18f, 18f)));
        Assert.That(GrottoOf(server).Monsters, Does.Contain(bosses[0]));
    }

    [Test]
    public void QuakeSlam_DoesNotStrikeAPlayerBehindAPillar()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = InTheChamber(2f, 2f);
        WorldPosition pillar = PillarIn(GrottoOf(server));
        PlayerEntity seen = server.PlayerOf(players[0]);
        PlayerEntity hidden = server.PlayerOf(players[1]);
        monarch.Position = new WorldPosition(pillar.X - 1.6f, 0f, pillar.Z);
        seen.Position = new WorldPosition(pillar.X - 3.4f, 0f, pillar.Z);
        hidden.Position = new WorldPosition(pillar.X + 1.6f, 0f, pillar.Z);
        server.Tick();

        BeginSlam(server, monarch, seen);
        LetTheSlamLand(server);

        Assert.That(seen.CurrentHealth, Is.LessThan(Spare));
        Assert.That(hidden.CurrentHealth, Is.EqualTo(Spare), "3.2 m away, but out of the boss's sight");
    }

    [Test]
    public void QuakeSlam_StrikesEveryPlayerWithinFourMetres_InOrderOfEntityId_AndNobodyBeyond()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = InTheChamber(2f, 3.9f, 4.5f);
        PlayerEntity[] entities = players.Select(server.PlayerOf).ToArray();
        server.Transport.ClearSent();

        BeginSlam(server, monarch, entities[0]);
        LetTheSlamLand(server);

        Assert.That(entities.Select(player => player.CurrentHealth < Spare), Is.EqualTo(new[] { true, true, false }));
        Assert.That(
            SlamsHeardBy(server, players[2]).Select(resolved => resolved.Target),
            Is.EqualTo(new[] { entities[0].Id, entities[1].Id }),
            "each struck player, in order of entity ID, told to every client that knows it");
        Assert.That(
            SlamsHeardBy(server, players[0]).Select(resolved => (resolved.Caster, resolved.Outcome)),
            Is.All.EqualTo((monarch.Id, SkillOutcome.Hit)));
    }

    [Test]
    public void QuakeSlam_WhenItsTargetDiesDuringTheCast_GoesOn_AndStrikesTheOthers()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = InTheChamber(2f, 3f);
        PlayerEntity target = server.PlayerOf(players[0]);
        PlayerEntity other = server.PlayerOf(players[1]);

        BeginSlam(server, monarch, target);
        server.Tick(TestServer.TickRate / 2);
        server.Combat.Kill(GrottoOf(server), target, monarch, server.CurrentTick);
        bool isStillCasting = monarch.Combat.IsCasting;
        LetTheSlamLand(server);

        Assert.That(isStillCasting, Is.True, "a cast on itself outlives the player it fights");
        Assert.That(other.CurrentHealth, Is.LessThan(Spare));
    }

    [Test]
    public void QuakeSlam_WhenItsTargetStepsOutBeforeTheCastEnds_StrikesNobody_TellsNobody_AndItsCooldownRuns()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = InTheChamber(2f);
        PlayerEntity player = server.PlayerOf(players[0]);
        var near = new WorldPosition(monarch.Position.X - 2f, 0f, monarch.Position.Z);
        server.Transport.ClearSent();

        BeginSlam(server, monarch, player);
        server.Tick(TestServer.TickRate);
        player.Position = new WorldPosition(monarch.Position.X - 4.5f, 0f, monarch.Position.Z);
        LetTheSlamLand(server);
        player.Position = near;
        bool isCoolingDown = !server.Combat.CanMonsterCast(
            GrottoOf(server),
            monarch,
            QuakeSlam,
            player,
            server.CurrentTick);

        Assert.That(player.CurrentHealth, Is.EqualTo(Spare));
        Assert.That(server.Transport.ControlOpcodesSentTo(players[0]), Has.None.EqualTo(MessageOpcode.SkillResolved));
        Assert.That(monarch.Combat.IsCasting, Is.False);
        Assert.That(isCoolingDown, Is.True, "the cast ended rather than being cut short, so its cooldown runs");
    }

    [Test]
    public void RespawnDelay_MovesTheRespawnByUpToItsSpread_DrawingOnlyForASpread()
    {
        var spread = new MonsterSpawn(Monarch, default, 0d, 1, 3_600_000, 600_000);
        var earliest = new ScriptedRandom(0, 0);
        var latest = new ScriptedRandom(0, 1_200_000);
        var none = new ScriptedRandom(0);

        long first = MonsterAiSystem.RespawnDelay(spread, earliest);
        long last = MonsterAiSystem.RespawnDelay(spread, latest);
        long plain = MonsterAiSystem.RespawnDelay(new MonsterSpawn(Monarch, default, 0d, 1, 20_000), none);
        long soonest = MonsterAiSystem.RespawnDelay(new MonsterSpawn(Monarch, default, 0d, 1, 500), none);

        Assert.That((first, last), Is.EqualTo((3_000_000L, 4_200_000L)));
        Assert.That(earliest.Bounds.Concat(latest.Bounds), Is.EqualTo(new[] { 1_200_001, 1_200_001 }));
        Assert.That((plain, soonest), Is.EqualTo((20_000L, 1000L)), "never under a second");
        Assert.That(none.Calls, Is.Zero, "a spawn with no spread draws nothing");
    }
}
}
