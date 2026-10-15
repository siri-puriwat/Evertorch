using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The holds (Gameplay Systems §9.1): a stun, a freeze, or a sleep stops a body's steps, swings, casts, and a
///     monster's AI, and refuses a player's commands by kind as a trade's are, but for equipping, unequipping, and an
///     open trade (owner decision 25); a silence takes the skills alone. A held player in a portal is carried over with
///     its ailments.
/// </summary>
[TestFixture]
public sealed class AilmentHoldTests
{
    private const string FirstAid = "skill.first_aid";
    private static readonly StatusDefinitionId Frozen = new("status.frozen");
    private static readonly StatusDefinitionId Stunned = new("status.stunned");
    private static readonly StatusDefinitionId Asleep = new("status.asleep");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");

    private static void Inflict(TestServer server, WorldEntity entity, StatusDefinitionId status, long durationMs)
    {
        server.StatusEffects.Apply(entity, status, default, server.World.NowMs + durationMs);
    }

    private static CommandRejectionReason RefusalOf(TestServer server, ConnectionId connection, uint sequence)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .Where(rejected => rejected.CommandSequence == sequence)
            .Select(rejected => rejected.Reason)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Single();
    }

    [TestCase("status.stunned")]
    [TestCase("status.frozen")]
    [TestCase("status.asleep")]
    public void Hold_StopsThePlayersSteps_UntilItEnds(string status)
    {
        var rig = new CombatRig();
        WorldPosition start = rig.Entity.Position;

        Inflict(rig.Server, rig.Entity, new StatusDefinitionId(status), 2000);
        rig.Move(0f, 1f);
        rig.Server.Tick(TestServer.TickRate);
        WorldPosition held = rig.Entity.Position;
        rig.Server.Tick(TestServer.TickRate + 1);
        rig.Move(0f, 1f);
        rig.Server.Tick(5);

        Assert.That(held, Is.EqualTo(start));
        Assert.That(rig.Entity.Position, Is.Not.EqualTo(start), "free again, it walks");
    }

    // Every hit lands at the least of its draws, criticals and dodges draw high, and every ailment's roll draws 0.
    private sealed class LandingDraws : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax == 1000 ? 999 : 0;
        }
    }

    // The player's swing lands first in the tick and its card stuns the slime, whose bite is due later in the same
    // tick: the bite is interrupted, not resolved (review of Milestone 19).
    [Test]
    public void AHold_LandedEarlierInATick_InterruptsACastDueLaterInIt()
    {
        var rig = new CombatRig(combatRandom: new LandingDraws());
        TestServer server = rig.Server;
        rig.Entity.CardBonus = new CardBonus(new PrimaryStats(0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0, 0, 0,
            inflict: new Dictionary<Ailment, int> { [Ailment.Stun] = 10_000 });
        server.Tick();
        long now = server.World.NowMs;
        rig.Entity.Combat.BeginSwing(rig.Slime.Id, server.CurrentTick, now + 10,
            new AttackTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        rig.Slime.Combat.BeginCast(new SkillDefinitionId("skill.venom_bite"), 1, rig.Entity.Id, now, now + 20, true);
        server.Transport.ClearSent();

        server.Tick();

        Assert.That(rig.Slime.HasAilment(Ailment.Stun), Is.True, "the swing's card stunned it");
        Assert.That(rig.Slime.Combat.IsCasting, Is.False);
        Assert.That(
            server.Transport.ControlOpcodesSentTo(rig.Player),
            Has.No.Member(MessageOpcode.SkillResolved),
            "the bite never resolved");
    }

    [Test]
    public void Hold_EndsThePlayersAutoAttack_AndASwingUnderWayLandsNothing()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        int health = rig.Slime.CurrentHealth;
        bool wasSwinging = rig.Entity.Combat.IsSwinging;

        Inflict(rig.Server, rig.Entity, Asleep, 10_000);
        rig.Server.Tick(5 * TestServer.TickRate);

        Assert.That(wasSwinging, Is.True);
        Assert.That(rig.Slime.CurrentHealth, Is.EqualTo(health), "no swing landed");
        Assert.That((rig.Entity.Combat.IsAutoAttacking, rig.Entity.Combat.IsSwinging), Is.EqualTo((false, false)));
    }

    [Test]
    public void Hold_InAPortal_StillCarriesThePlayerOver_WithItsAilments()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId player = server.EnterWorld(1);
        PlayerEntity entity = server.PlayerOf(player);
        MapInstance town = server.SessionOf(player).Map!;
        MapPortal portal = town.Definition.Portals.First();
        Inflict(server, entity, Frozen, 30_000);

        entity.Position = portal.Center;
        server.Tick(2);

        Assert.That(server.SessionOf(player).Map!.Definition.Id, Is.EqualTo(portal.DestinationMap));
        Assert.That(entity.HasAilment(Ailment.Freeze), Is.True);
    }

    [Test]
    public void Hold_OnAMonster_StopsItsSwingsAndItsAi_AndItKeepsItsTarget()
    {
        var server = new TestServer(withMonsters: true, combatRandom: new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        MapInstance map = server.World.Maps.Single();
        MonsterEntity slime = server.MonstersNear(map.Definition.SpawnPosition).First();
        PlayerEntity entity = server.PlayerOf(player);
        entity.Position = new WorldPosition(slime.Position.X + 1f, slime.Position.Y, slime.Position.Z);
        slime.Target = entity.Id;
        slime.Combat.IsAutoAttacking = true;
        Inflict(server, slime, Frozen, 3000);
        int health = entity.CurrentHealth;
        long decisions = slime.Brain.Decisions;
        bool hasSwung = false;
        server.AfterEachTick = () => hasSwung |= slime.Combat.IsSwinging;

        server.Tick(2 * TestServer.TickRate);

        Assert.That((hasSwung, entity.CurrentHealth), Is.EqualTo((false, health)), "no swing began");
        Assert.That(slime.Brain.Decisions, Is.EqualTo(decisions), "the AI did not decide");
        Assert.That((slime.Target, slime.Combat.IsAutoAttacking), Is.EqualTo((entity.Id, true)));
        Assert.That(server.Combat.CanMonsterCast(map, slime, new SkillDefinitionId("skill.spark_bolt"), entity,
            server.CurrentTick), Is.False);
    }

    [Test]
    public void Hold_RefusesThePlayersCommandsByKind_ButEquippingChatAndCancelling()
    {
        var rig = new CombatRig();
        TestServer server = rig.Server;
        ConnectionId player = rig.Player;
        server.SendEquip(player, 987_654, 1);
        server.Tick();
        CommandRejectionReason freeEquip = RefusalOf(server, player, 1);
        server.Transport.ClearSent();

        Inflict(server, rig.Entity, Stunned, 60_000);
        server.SendAttack(player, rig.Slime.Id, 2);
        server.SendUseSkill(player, FirstAid, default, 3);
        server.SendPickup(player, new EntityId(987_654), 4);
        server.SendUseItem(player, 987_654, 5);
        server.SendTradeRequest(player, "Nobody", 6);
        server.SendEquip(player, 987_654, 7);
        server.SendChat(player, ChatChannel.Nearby, string.Empty, "held", 8);
        server.SendCancel(player, 9);
        server.Tick();

        Assert.That(
            new uint[] { 2, 3, 4, 5, 6 }.Select(sequence => RefusalOf(server, player, sequence)),
            Is.All.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(RefusalOf(server, player, 7), Is.EqualTo(freeEquip), "equipping answers as it would free");
        Assert.That(
            (RefusalOf(server, player, 8), RefusalOf(server, player, 9)),
            Is.EqualTo((CommandRejectionReason.None, CommandRejectionReason.None)));
    }

    [Test]
    public void Silence_OnAMonster_StopsItsCastsBeforeTheyDraw()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        server.World.TryGetMap(new MapDefinitionId("map.umbral_grotto"), out MapInstance? grotto);
        MonsterEntity wisp = grotto!.Monsters.First(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        PlayerEntity entity = server.PlayerOf(player);
        entity.Position = new WorldPosition(12f, 0f, -19.5f);
        wisp.Position = new WorldPosition(16f, 0f, -19.5f);
        var spark = new SkillDefinitionId("skill.numbing_spark");
        bool free = server.Combat.CanMonsterCast(grotto, wisp, spark, entity, server.CurrentTick);

        Inflict(server, wisp, Silenced, 15_000);

        Assert.That(free, Is.True);
        Assert.That(server.Combat.CanMonsterCast(grotto, wisp, spark, entity, server.CurrentTick), Is.False);
    }

    [Test]
    public void Silence_RefusesSkillsAlone_AndEndsACastUnderWay()
    {
        var rig = new CombatRig();
        TestServer server = rig.Server;
        rig.Entity.CurrentHealth = 1;
        server.SendUseSkill(rig.Player, FirstAid, default, 1);
        server.Tick();
        bool wasCasting = rig.Entity.Combat.IsCasting;

        Inflict(server, rig.Entity, Silenced, 60_000);
        server.Tick();
        bool isCasting = rig.Entity.Combat.IsCasting;
        server.SendUseSkill(rig.Player, FirstAid, default, 2);
        server.SendAttack(rig.Player, rig.Slime.Id, 3);
        server.Tick();

        Assert.That((wasCasting, isCasting), Is.EqualTo((true, false)), "the cast was interrupted");
        Assert.That(RefusalOf(server, rig.Player, 2), Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(RefusalOf(server, rig.Player, 3), Is.EqualTo(CommandRejectionReason.None), "it may still swing");
        Assert.That(rig.Entity.Combat.IsAutoAttacking, Is.True);
    }
}
}
