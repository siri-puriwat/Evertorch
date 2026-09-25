using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Status effects on players (Gameplay Systems §9.1; Network Protocol §9): Focus raises AGI and DEX, a recast
///     renews it, it ends at its time and on death, it survives an attach, and only its owner hears of it.
/// </summary>
[TestFixture]
public sealed class StatusEffectTests
{
    private const string Focus = "skill.focus";
    private static readonly StatusDefinitionId FocusStatus = new("status.focus");

    private static List<StatusEffects> StatusLists(TestServer server, ConnectionId player)
    {
        var lists = new List<StatusEffects>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.StatusEffects
                && StatusEffects.TryRead(message.Payload, out StatusEffects? read)
                && read != null)
            {
                lists.Add(read);
            }
        }

        return lists;
    }

    private static (string Status, uint RemainingMs)[] Shown(StatusEffects list)
    {
        return list.Effects.Select(effect => (effect.Status.Value, effect.RemainingMs)).ToArray();
    }

    // The time of the tick last run, as the simulation counts it.
    private static long Now(TestServer server)
    {
        return (long)(server.CurrentTick - 1) * 1000 / TestServer.TickRate;
    }

    [Test]
    public void Attach_KeepsTheEffect_AndTheBaselineCarriesWhatIsLeftOfIt()
    {
        var server = new TestServer(reconnectGraceMs: 60_000);
        ConnectionId first = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(first);
        server.SendUseSkill(first, Focus, default, 1);
        server.Tick(20);
        server.Disconnect(first);
        server.Tick();

        ConnectionId again = server.EnterWorld(1);

        Assert.That(server.PlayerOf(again), Is.SameAs(player));
        Assert.That(player.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { FocusStatus }));
        StatusEffectEntry shown = StatusLists(server, again).Single().Effects.Single();
        Assert.That(shown.Status, Is.EqualTo(FocusStatus));
        Assert.That(shown.RemainingMs, Is.InRange(58_000u, 59_000u), "a second or so has passed");
    }

    [Test]
    public void Death_EndsEveryEffect_AndTheStatisticsReturn()
    {
        var rig = new CombatRig();
        rig.Server.StatusEffects.Apply(rig.Entity, FocusStatus, Now(rig.Server) + 60_000);
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();

        rig.Server.Combat.Kill(rig.Map, rig.Entity, rig.Slime, rig.Server.CurrentTick);
        rig.Server.Tick();

        Assert.That(rig.Entity.IsDead, Is.True);
        Assert.That(rig.Entity.StatusEffects, Is.Empty);
        Assert.That(rig.Entity.Stats.AttackSpeed, Is.EqualTo(153));
        Assert.That(StatusLists(rig.Server, rig.Player).Single().Effects, Is.Empty);
    }

    [Test]
    public void Effect_WhenItsTimeIsUp_Ends_AndTheStatisticsReturn()
    {
        var rig = new CombatRig();
        rig.Server.StatusEffects.Apply(rig.Entity, FocusStatus, Now(rig.Server) + 100);
        int raised = rig.Entity.Stats.AttackSpeed;
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();

        rig.Server.Tick(2);

        Assert.That(raised, Is.EqualTo(154));
        Assert.That(rig.Entity.StatusEffects, Is.Empty);
        Assert.That((rig.Entity.Stats.AttackSpeed, rig.Entity.Stats.Hit), Is.EqualTo((153, 182)));
        Assert.That(StatusLists(rig.Server, rig.Player).Single().Effects, Is.Empty, "the owner hears it ended");
    }

    [Test]
    public void Focus_CastAgain_RenewsItToEndAMinuteAfterTheRecast()
    {
        var rig = new CombatRig();
        rig.Server.SendUseSkill(rig.Player, Focus, default, 1);
        rig.Server.Tick(20);
        rig.Entity.CurrentSpirit = 24;
        rig.Server.Transport.ClearSent();

        rig.Server.SendUseSkill(rig.Player, Focus, default, 2);
        rig.Server.Tick();

        Assert.That(rig.Entity.StatusEffects.Single().EndMs, Is.EqualTo(Now(rig.Server) + 60_000));
        Assert.That(
            Shown(StatusLists(rig.Server, rig.Player).Single()),
            Is.EqualTo(new[] { ("status.focus", 60_000u) }),
            "the renewed effect has a whole minute again");
        Assert.That(rig.Entity.Stats.AttackSpeed, Is.EqualTo(154), "one Focus, not two");
    }

    [Test]
    public void Focus_RaisesAgiAndDex_AndOnlyItsOwnerHearsOfIt()
    {
        var rig = new CombatRig();
        ConnectionId observer = rig.Server.EnterWorld(2);
        rig.Server.Tick(2);
        rig.Server.Transport.ClearSent();
        DerivedStats before = rig.Entity.Stats;

        rig.Server.SendUseSkill(rig.Player, Focus, default, 1);
        rig.Server.Tick();

        Assert.That((before.AttackSpeed, before.Hit), Is.EqualTo((153, 182)));
        Assert.That((rig.Entity.Stats.AttackSpeed, rig.Entity.Stats.Hit), Is.EqualTo((154, 187)), "the vector");
        Assert.That(rig.Entity.Primary, Is.EqualTo(new PrimaryStats(5, 5, 5, 5, 5, 5)), "the stored base");
        Assert.That(rig.Entity.CurrentSpirit, Is.EqualTo(9));
        Assert.That(
            Shown(StatusLists(rig.Server, rig.Player).Single()),
            Is.EqualTo(new[] { ("status.focus", 60_000u) }));
        Assert.That(StatusLists(rig.Server, observer), Is.Empty);
        SkillResolved seen = rig.Server.Transport.ControlSentTo(observer)
            .Where(message => message.Opcode == MessageOpcode.SkillResolved)
            .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? read) ? read! : null)
            .Single()!;
        Assert.That((seen.Target, seen.Outcome, seen.Amount), Is.EqualTo((rig.Entity.Id, SkillOutcome.Applied, 0u)));
    }
}
}
