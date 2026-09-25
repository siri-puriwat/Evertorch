using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     <c>UseSkill</c> over the wire (Network Protocol §8, §9, §11): each refusal reason in the order of the checks, the
///     cast and its result to everyone who knows the caster or the target, and the owner's skill list.
/// </summary>
[TestFixture]
public sealed class SkillCommandTests
{
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";

    private static CommandRejectionReason[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .ToArray();
    }

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryRead<T> read)
        where T : class
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T? decoded) && decoded != null)
            {
                messages.Add(decoded);
            }
        }

        return messages;
    }

    private delegate bool TryRead<T>(byte[] payload, out T? message);

    private static List<SkillList> SkillLists(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.SkillList, (byte[] bytes, out SkillList? read) =>
            SkillList.TryRead(bytes, out read));
    }

    [Test]
    public void FirstAid_TellsTheCasterTheNominalHealAndItsHpAndSp()
    {
        var rig = new CombatRig();
        rig.Entity.CurrentHealth = 60;

        rig.Server.SendUseSkill(rig.Player, FirstAid, default, 1);
        rig.Server.Tick(30);

        SkillCastStarted started = Sent(
            rig.Server,
            rig.Player,
            MessageOpcode.SkillCastStarted,
            (byte[] bytes, out SkillCastStarted? read) => SkillCastStarted.TryRead(bytes, out read)).Single();
        SkillResolved resolved = Sent(
            rig.Server,
            rig.Player,
            MessageOpcode.SkillResolved,
            (byte[] bytes, out SkillResolved? read) => SkillResolved.TryRead(bytes, out read)).Single();
        Assert.That((started.Target, started.CastMs), Is.EqualTo((default(EntityId), 1331u)), "on itself");
        Assert.That((resolved.Target, resolved.Outcome, resolved.Amount),
            Is.EqualTo((rig.Entity.Id, SkillOutcome.Healed, 15u)), "the nominal 15, though only 11 were missing");
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(71));
    }

    [Test]
    public void SkillList_EndsTheBaseline_AndComesAgainWithTheCooldownLeftAfterACast()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false, combatRandom: new SureHitRandom());
        ConnectionId player = server.EnterWorld(1);
        SkillList baseline = SkillLists(server, player).Single();
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        server.PlayerOf(player).Position = new WorldPosition(slime.Position.X + 1f, slime.Position.Y, slime.Position.Z);
        server.Tick();
        server.Transport.ClearSent();

        server.SendUseSkill(player, Strike, slime.Id, 1);
        server.Tick();

        Assert.That(
            baseline.Skills.Select(entry => (entry.Skill.Value, entry.Range, entry.SpCost, entry.CooldownMs,
                entry.AfterCastDelayMs, entry.RemainingCooldownMs)),
            Is.EqualTo(new[] { (Strike, 1.5f, 8u, 2000u, 500u, 0u), (FirstAid, 0f, 3u, 0u, 0u, 0u) }));
        SkillListEntry strike = SkillLists(server, player).Single().Skills.First();
        Assert.That(strike.RemainingCooldownMs, Is.EqualTo(2000u), "sent in the tick the cast resolved");
    }

    [Test]
    public void SkillList_OnAReconnect_CarriesWhatIsLeftOfTheCooldown()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false, reconnectGraceMs: 60_000);
        ConnectionId first = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(first);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        player.Position = new WorldPosition(slime.Position.X + 1f, slime.Position.Y, slime.Position.Z);
        server.Tick();
        server.SendUseSkill(first, Strike, slime.Id, 1);
        server.Tick(10);
        server.Disconnect(first);
        server.Tick();

        ConnectionId again = server.EnterWorld(1);

        SkillListEntry strike = SkillLists(server, again).Single().Skills.First();
        Assert.That(strike.RemainingCooldownMs, Is.InRange(1u, 1500u), "the cooldown survives an attach");
    }

    [Test]
    public void Strike_IsToldToTheCasterAndAnObserver_CastThenResult()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        ConnectionId observer = rig.Server.EnterWorld(2);
        rig.Server.Tick(2);
        rig.Server.Transport.ClearSent();

        rig.Server.SendUseSkill(rig.Player, Strike, rig.Slime.Id, 1);
        rig.Server.Tick();

        foreach (ConnectionId viewer in new[] { rig.Player, observer })
        {
            SkillCastStarted started = Sent(
                rig.Server,
                viewer,
                MessageOpcode.SkillCastStarted,
                (byte[] bytes, out SkillCastStarted? read) => SkillCastStarted.TryRead(bytes, out read)).Single();
            SkillResolved resolved = Sent(
                rig.Server,
                viewer,
                MessageOpcode.SkillResolved,
                (byte[] bytes, out SkillResolved? read) => SkillResolved.TryRead(bytes, out read)).Single();
            Assert.That((started.Caster, started.Target, started.CastMs),
                Is.EqualTo((rig.Entity.Id, rig.Slime.Id, 0u)));
            Assert.That((resolved.Caster, resolved.Target, resolved.Outcome, resolved.Amount),
                Is.EqualTo((rig.Entity.Id, rig.Slime.Id, SkillOutcome.Hit, 17u)));
            Assert.That(resolved.TargetHealthPermille, Is.EqualTo(HealthRatio.ToPermille(33, 50)));
            MessageOpcode[] order = rig.Server.Transport.ControlOpcodesSentTo(viewer)
                .Where(opcode => opcode is MessageOpcode.SkillCastStarted or MessageOpcode.SkillResolved)
                .ToArray();
            Assert.That(order, Is.EqualTo(new[] { MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved }));
        }
    }

    [Test]
    public void UseSkill_OutOfRange_IsReason2()
    {
        var rig = new CombatRig();
        rig.StandBeside(rig.Slime, 3f);

        rig.Server.SendUseSkill(rig.Player, Strike, rig.Slime.Id, 1);
        rig.Server.Tick();

        Assert.That(Rejections(rig.Server, rig.Player), Is.EqualTo(new[] { CommandRejectionReason.OutOfRange }));
    }

    [Test]
    public void UseSkill_RefusedForSp_IsAuditedButNeverScored()
    {
        var rig = new CombatRig();
        rig.Entity.CurrentSpirit = 0;

        for (uint sequence = 1; sequence <= 5; sequence++)
        {
            rig.Server.SendUseSkill(rig.Player, Strike, rig.Slime.Id, sequence);
            rig.Server.Tick();
        }

        Assert.That(Rejections(rig.Server, rig.Player),
            Has.Length.EqualTo(5).And.All.EqualTo(CommandRejectionReason.NotEnoughSp));
        Assert.That(rig.Server.SessionOf(rig.Player).Violations!.Value, Is.Zero);
        Assert.That(
            rig.Server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(5));
    }

    [Test]
    public void UseSkill_ThatIsRefused_IsAnsweredWithEachReasonInTheOrderOfTheChecks()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;
        ConnectionId player = rig.Player;
        TestServer server = rig.Server;

        server.SendUseSkill(player, "skill.basic_attack", rig.Slime.Id, 1);
        rig.Entity.CurrentSpirit = 7;
        server.Tick();
        server.SendUseSkill(player, Strike, default, 2);
        server.Tick();
        rig.Entity.CurrentSpirit = 24;
        server.SendUseSkill(player, Strike, rig.Entity.Id, 3);
        server.SendUseSkill(player, FirstAid, rig.Slime.Id, 4);
        rig.StandBeside(rig.Slime, 3f);
        server.Tick();
        rig.StandBeside(rig.Slime, 1.2f);
        server.SendUseSkill(player, Strike, rig.Slime.Id, 5);
        server.Tick();
        server.SendUseSkill(player, Strike, rig.Slime.Id, 6);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    CommandRejectionReason.NotAllowedNow, CommandRejectionReason.NotEnoughSp,
                    CommandRejectionReason.InvalidTarget, CommandRejectionReason.InvalidTarget,
                    CommandRejectionReason.NotAllowedNow
                }),
            "a skill the job lacks, too little SP, another player, the self skill at a monster, then the cooldown");
    }
}
}
