using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The spark wisp on the training field (Gameplay Systems §10; the monster AI and skills research notes' vectors):
///     it attacks from its range, walks away from a target nearer than its keep distance to the first free point, never
///     while it swings or casts or beyond its leash, and casts Spark Bolt, magic damage that never misses.
/// </summary>
[TestFixture]
public sealed class RangedMonsterTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
    private static readonly SkillDefinitionId SparkBolt = new("skill.spark_bolt");

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

    // A player on the field and one wisp, still idle this soon after it spawned, where the test puts it; every other
    // monster is moved far off.
    private static (TestServer Server, MonsterEntity Wisp, ConnectionId Player) OnTheField(WorldPosition wispAt)
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, combatRandom: new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        server.PlayerOf(player).CurrentHealth = 1_000_000;
        server.PlayerOf(player).Position = MapOf(server, Ground).Definition.Portals.Single().Center;
        server.Tick(2);
        Assert.That(server.SessionOf(player).Character!.Map.Definition.Id, Is.EqualTo(Field), "crossed");

        MonsterEntity[] monsters = MapOf(server, Field).Monsters.ToArray();
        MonsterEntity wisp = monsters.First(monster => monster.Definition.Id.Value == "monster.spark_wisp");
        foreach (MonsterEntity other in monsters.Where(monster => monster != wisp))
        {
            other.Position = new WorldPosition(20f, 0f, 20f);
        }

        wisp.Position = wispAt;
        Assert.That(wisp.Brain.State, Is.EqualTo(MonsterAiState.Idle));
        return (server, wisp, player);
    }

    // The wisp turns on the player as if it had just been hit, at its next decision.
    private static void Provoke(TestServer server, MonsterEntity wisp, ConnectionId player, WorldPosition playerAt)
    {
        server.PlayerOf(player).Position = playerAt;
        wisp.Brain.LastAttacker = server.PlayerOf(player).Id;
        server.Tick(3);
        Assert.That(wisp.Target, Is.EqualTo(server.PlayerOf(player).Id), "the wisp turned on the player");
    }

    [TestCase(249_999, true)]
    [TestCase(250_000, false)]
    public void IsTried_WithAQuarterChance_CastsBelowAQuarterOfTheScale(int draw, bool isTried)
    {
        Assert.That(MonsterAiSystem.IsTried(0.25, draw), Is.EqualTo(isTried));
    }

    // Keep 3.5 m and range 6 m put the goal (3.5 + 6) / 2 = 4.75 m from the target. Straight away from a target 1 m
    // east of the wisp at its home; then, with the straight goal inside the obstacle at (4.5, -6.5), the away direction
    // turned by +45 degrees to (-0.707, -0.707).
    [TestCase(10f, -10f, 11f, -10f, 6.25f, -10f)]
    [TestCase(8.25f, -6.5f, 9.25f, -6.5f, 5.891243f, -9.858757f)]
    public void Wisp_WithATargetTooClose_WalksToTheFirstFreePointAwayFromIt(
        float wispX,
        float wispZ,
        float targetX,
        float targetZ,
        float goalX,
        float goalZ)
    {
        (TestServer server, MonsterEntity wisp, ConnectionId player) = OnTheField(new WorldPosition(wispX, 0f, wispZ));
        Provoke(server, wisp, player, new WorldPosition(targetX, 0f, targetZ));

        WorldPosition goal = wisp.Brain.Path.Waypoints.Last();
        Assert.That(wisp.Brain.IsRetreating, Is.True);
        Assert.That(goal.X, Is.EqualTo(goalX).Within(1e-3f));
        Assert.That(goal.Z, Is.EqualTo(goalZ).Within(1e-3f));

        server.Tick(40);
        Assert.That(
            Distance(wisp.Position, goal),
            Is.LessThan(0.3f),
            "it walked there although the target was in reach");
        Assert.That(wisp.Brain.IsRetreating, Is.False);
    }

    [Test]
    public void Wisp_FromItsRange_AttacksAndCastsSparkBolt_AMagicHitThatNeverMisses()
    {
        (TestServer server, MonsterEntity wisp, ConnectionId player) = OnTheField(new WorldPosition(10f, 0f, -10f));
        server.Transport.ClearSent();
        Provoke(server, wisp, player, new WorldPosition(15.5f, 0f, -10f));
        WorldPosition standing = wisp.Position;

        SkillResolved? bolt = null;
        for (int tick = 0; tick < 600 && bolt == null; tick++)
        {
            server.Tick();
            bolt = server.Transport.ControlSentTo(player)
                .Where(message => message.Opcode == MessageOpcode.SkillResolved)
                .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? read) ? read : null)
                .FirstOrDefault(read => read != null && read.Skill == SparkBolt);
        }

        Assert.That(bolt, Is.Not.Null, "it cast Spark Bolt within 30 s");
        SkillCastStarted cast = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.SkillCastStarted)
            .Select(message => SkillCastStarted.TryRead(message.Payload, out SkillCastStarted? read) ? read! : null!)
            .First(read => read.Skill == SparkBolt);
        Assert.That((cast.Caster, cast.Target, cast.CastMs), Is.EqualTo((wisp.Id, server.PlayerOf(player).Id, 1500u)));
        Assert.That((bolt!.Caster, bolt.Outcome), Is.EqualTo((wisp.Id, SkillOutcome.Hit)));
        Assert.That(bolt.Amount, Is.InRange(9u, 17u), "magic attack 20 +-20 %, less the player's soft magic defense 7");
        Assert.That(
            server.Transport.ControlOpcodesSentTo(player),
            Does.Contain(MessageOpcode.AttackStarted),
            "its ranged basic attack");
        Assert.That(Distance(wisp.Position, standing), Is.LessThan(1e-3f), "5.5 m is within its reach");
    }

    [Test]
    public void Wisp_WhileCasting_DoesNotRetreat()
    {
        (TestServer server, MonsterEntity wisp, ConnectionId player) = OnTheField(new WorldPosition(10f, 0f, -10f));
        Provoke(server, wisp, player, new WorldPosition(15f, 0f, -10f));
        PlayerEntity target = server.PlayerOf(player);
        for (int tick = 0; tick < 100 && !wisp.Combat.IsCasting; tick++)
        {
            if (server.Combat.CanMonsterCast(MapOf(server, Field), wisp, SparkBolt, target, server.CurrentTick))
            {
                server.Combat.BeginMonsterCast(MapOf(server, Field), wisp, SparkBolt, target, server.CurrentTick);
            }
            else
            {
                server.Tick();
            }
        }

        Assert.That(wisp.Combat.IsCasting, Is.True, "casting between its swings");

        WorldPosition castingAt = wisp.Position;
        server.PlayerOf(player).Position = new WorldPosition(castingAt.X + 1f, 0f, castingAt.Z);
        while (wisp.Combat.IsCasting)
        {
            server.Tick();
            Assert.That(wisp.Brain.IsRetreating, Is.False, "the cast holds it");
            Assert.That(wisp.Position, Is.EqualTo(castingAt));
        }

        server.Tick(3);
        Assert.That(wisp.Brain.IsRetreating || wisp.Combat.IsSwinging, Is.True, "once the cast is over it acts again");
    }

    // At 13.9 m from its home (10, 0, -10), with the target 0.4 m nearer home: straight away and at +-45 degrees the
    // goal lies 17-18 m from home, at +-90 degrees sqrt(13.5^2 + 4.75^2) = 14.3 m, all beyond the 14 m leash.
    [Test]
    public void Wisp_WhoseEveryRetreatLiesBeyondItsLeash_StaysAndAttacks()
    {
        (TestServer server, MonsterEntity wisp, ConnectionId player) = OnTheField(new WorldPosition(-3.9f, 0f, -10f));
        Provoke(server, wisp, player, new WorldPosition(-3.5f, 0f, -10f));

        for (int tick = 0; tick < 20 && !wisp.Combat.IsSwinging && !wisp.Combat.IsCasting; tick++)
        {
            Assert.That(wisp.Brain.IsRetreating, Is.False);
            server.Tick();
        }

        Assert.That(wisp.Combat.IsSwinging || wisp.Combat.IsCasting, Is.True, "it attacks from where it stands");
        Assert.That(Distance(wisp.Position, new WorldPosition(-3.9f, 0f, -10f)), Is.LessThan(1e-3f));
    }
}
}
