using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Spending stat points (Gameplay Systems §2): <see cref="AllocateStat" /> raises one primary statistic by its
///     steps, whole or not at all, derives the statistics again once, and answers with the sheet; a raise past 99 is
///     refused before points that fall short.
/// </summary>
[TestFixture]
public sealed class StatAllocationTests
{
    private const long Character = 1;

    // Levels 2 to 10 grant 3, 3, 3, 4, 4, 4, 4, 4, and 5 points.
    private const int PointsAtLevelTen = 34;

    private static (TestServer Server, ConnectionId Player) AtLevel(int level)
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(Character);
        server.PlayerOf(player).Level = level;
        server.Tick();
        server.Transport.ClearSent();
        return (server, player);
    }

    private static List<CharacterSheet> Sheets(TestServer server, ConnectionId player)
    {
        var sheets = new List<CharacterSheet>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.CharacterSheet
                && CharacterSheet.TryRead(message.Payload, out CharacterSheet? sheet))
            {
                sheets.Add(sheet!);
            }
        }

        return sheets;
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    // Raises cost 2 up to 10, 3 from 11 to 20, and 4 from 21 (Gameplay Systems §2): 5 to 25 would cost 58.
    [TestCase(1, 5, PrimaryStat.Agi, 1, CommandRejectionReason.NotEnoughPoints,
        TestName = "AllocateStat_AtLevelOne_IsRefusedForThePoints")]
    [TestCase(10, 5, PrimaryStat.Agi, 20, CommandRejectionReason.NotEnoughPoints,
        TestName = "AllocateStat_BeyondThePoints_IsRefusedWhole")]
    [TestCase(1, 98, PrimaryStat.Agi, 2, CommandRejectionReason.RequirementNotMet,
        TestName = "AllocateStat_PastTheCap_IsRefusedBeforeThePoints")]
    [TestCase(10, 99, PrimaryStat.Agi, 1, CommandRejectionReason.RequirementNotMet,
        TestName = "AllocateStat_AtTheCap_IsRefused")]
    public void AllocateStat_ThatCannotBeMadeWhole_ChangesNothing(
        int level,
        int agility,
        PrimaryStat stat,
        int steps,
        CommandRejectionReason expected)
    {
        (TestServer server, ConnectionId player) = AtLevel(level);
        var start = new PrimaryStats(5, agility, 5, 5, 5, 5);
        server.PlayerOf(player).SetPrimary(start);
        server.Tick();
        server.Transport.ClearSent();

        server.SendAllocateStat(player, stat, (byte)steps, 7);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejection => (rejection.CommandSequence, rejection.Reason)),
            Is.EqualTo(new[] { (7u, expected) }));
        Assert.That(server.PlayerOf(player).Primary, Is.EqualTo(start));
        Assert.That(Sheets(server, player), Is.Empty, "nothing changed");
        Assert.That(server.BuildsLog.Entries, Is.Empty);
    }

    [Test]
    public void AllocateStat_IsKeptByTheNextCheckpoint()
    {
        (TestServer server, ConnectionId player) = AtLevel(10);
        server.SendAllocateStat(player, PrimaryStat.Dex, 4, 1);
        server.Tick();
        int before = server.Store.Checkpoints.Count;

        server.Lifetime.QueueCheckpoint(server.SessionOf(player).Character!);
        server.TickUntil(() => server.Store.Checkpoints.Count > before);

        CharacterCheckpoint checkpoint = server.Store.Checkpoints.Last();
        Assert.That(checkpoint.Stats, Is.EqualTo(new PrimaryStats(5, 5, 5, 5, 9, 5)));
        Assert.That(server.Store.Stored(Character).Stats, Is.EqualTo(new PrimaryStats(5, 5, 5, 5, 9, 5)));
    }

    // AGI and DEX shorten the swings that begin after the raise by what the Rules make of the new attack speed (Gameplay
    // Systems §2, §7); a swing already under way keeps its interval.
    [Test]
    public void AllocateStat_OfAgiAndDex_ShortensTheNextSwings_ByTheRulesValue()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Entity.Level = 10;
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);
        rig.Server.TickUntil(() => !rig.Entity.Combat.IsSwinging);
        int swingsBefore = rig.Starts().Count;

        rig.Server.SendAllocateStat(rig.Player, PrimaryStat.Agi, 10, 100);
        rig.Server.SendAllocateStat(rig.Player, PrimaryStat.Dex, 5, 101);
        rig.Server.Tick(60);

        DerivedStats raised = new CharacterStats(new RenewalCharacterRules())
            .Calculate(rig.Server.Content.Jobs[rig.Entity.Job], 10, new PrimaryStats(5, 15, 5, 5, 10, 5));
        TimeSpan interval = new RenewalCombatRules()
            .CalculateAttackTiming(AttackContext.ForAttackSpeed(raised.AttackSpeed))
            .Interval;
        List<AttackStarted> starts = rig.Starts();
        Assert.That(rig.Entity.Primary, Is.EqualTo(new PrimaryStats(5, 15, 5, 5, 10, 5)), "34 points spent");
        Assert.That(interval, Is.LessThan(TimeSpan.FromMilliseconds(940)));
        Assert.That(
            starts.Take(swingsBefore).Select(start => start.Timing.Interval),
            Is.All.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(starts.Count, Is.GreaterThan(swingsBefore + 1));
        Assert.That(starts.Skip(swingsBefore).Select(start => start.Timing.Interval), Is.All.EqualTo(interval));
    }

    // VIT raises the maximum HP, which the owner hears of at once as any other change of its HP; the current HP stays.
    [Test]
    public void AllocateStat_OfVit_RaisesTheMaximumHp_AndSendsTheHealth()
    {
        (TestServer server, ConnectionId player) = AtLevel(10);
        PlayerEntity entity = server.PlayerOf(player);
        int current = entity.CurrentHealth;
        DerivedStats expected = new CharacterStats(new RenewalCharacterRules())
            .Calculate(server.Content.Jobs[entity.Job], 10, new PrimaryStats(5, 5, 15, 5, 5, 5));

        server.SendAllocateStat(player, PrimaryStat.Vit, 10, 1);
        server.Tick();

        CharacterHealth[] health = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CharacterHealth)
            .Select(message =>
            {
                CharacterHealth.TryRead(message.Payload, out CharacterHealth read);
                return read;
            })
            .ToArray();
        Assert.That(entity.MaxHealth, Is.EqualTo(expected.MaxHp));
        Assert.That(entity.CurrentHealth, Is.EqualTo(current));
        Assert.That(
            health.Select(message => (message.Current, message.Maximum)),
            Is.EqualTo(new[] { ((uint)current, (uint)expected.MaxHp) }));
    }

    [Test]
    public void AllocateStat_WhileDeadOrLoggingOut_IsRefusedAsNotAllowedNow()
    {
        (TestServer dead, ConnectionId deadPlayer) = AtLevel(10);
        dead.Combat.Kill(dead.World.Maps.Single(), dead.PlayerOf(deadPlayer), null, dead.CurrentTick);
        (TestServer leaving, ConnectionId leavingPlayer) = AtLevel(10);
        leaving.SessionOf(leavingPlayer).Character!.IsLoggingOut = true;

        dead.SendAllocateStat(deadPlayer, PrimaryStat.Vit, 1, 1);
        dead.Tick();
        leaving.SendAllocateStat(leavingPlayer, PrimaryStat.Vit, 1, 1);
        leaving.Tick();

        Assert.That(Rejections(dead, deadPlayer).Single().Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(Rejections(leaving, leavingPlayer).Single().Reason,
            Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(dead.PlayerOf(deadPlayer).Primary.Vit, Is.EqualTo(5));
        Assert.That(leaving.PlayerOf(leavingPlayer).Primary.Vit, Is.EqualTo(5));
    }

    [Test]
    public void AllocateStat_WithThePoints_RaisesTheStatistic_AndTheSheetAnswersInTheSameTick()
    {
        (TestServer server, ConnectionId player) = AtLevel(10);

        server.SendAllocateStat(player, PrimaryStat.Agi, 3, 1);
        server.Tick();

        PlayerEntity entity = server.PlayerOf(player);
        CharacterSheet sheet = Sheets(server, player).Single();
        Assert.That(entity.Primary, Is.EqualTo(new PrimaryStats(5, 8, 5, 5, 5, 5)));
        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(sheet.Stats[1], Is.EqualTo(new CharacterSheetStat(8, 2)));
        Assert.That(sheet.StatPoints, Is.EqualTo(PointsAtLevelTen - 6), "three raises of 2 points");
        Assert.That(sheet.Flee, Is.EqualTo(entity.Stats.Flee), "the derived statistics follow");
        (LogLevel level, EventId eventId, string _, IReadOnlyDictionary<string, object?> fields) raised =
            server.BuildsLog.Entries.Single();
        Assert.That((raised.level, raised.eventId.Id, raised.eventId.Name),
            Is.EqualTo((LogLevel.Information, 1012, "StatRaised")));
        Assert.That(
            (raised.fields["Character"], raised.fields["Stat"], raised.fields["Previous"], raised.fields["Value"]),
            Is.EqualTo(((object?)Character, (object?)PrimaryStat.Agi, (object?)5, (object?)8)));
    }
}
}
