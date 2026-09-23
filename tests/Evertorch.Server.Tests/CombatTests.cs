using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class CombatTests
{
    // The adventurer's attack speed is 153: motion 470 ms, interval 940 ms. At 20 Hz a tick is 50 ms.
    private const int ImpactTicks = 10;

    private static void AssertGapsFollowTheInterval(IReadOnlyList<AttackStarted> starts)
    {
        for (int index = 1; index < starts.Count; index++)
        {
            uint gap = starts[index].StartTick - starts[index - 1].StartTick;
            Assert.That(gap, Is.InRange(18u, 19u), $"gap {index} in ticks");
        }
    }

    [Test]
    public void Attack_BySeveralPlayersOnOneTick_ResolvesImpactsInAFixedOrder()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Entity.CurrentHealth = 1;
        rig.Slime.CurrentHealth = 1;
        var timing = new AttackTiming(
            TimeSpan.FromMilliseconds(1200),
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(300));
        uint now = rig.Server.CurrentTick;
        long nowMs = (now - 1) * 50L;

        // Both impacts fall due on the next tick; the slime's is 10 ms earlier, so it resolves first and its
        // attacker's pending swing is interrupted.
        rig.Slime.Target = rig.Entity.Id;
        rig.Slime.Combat.BeginSwing(rig.Entity.Id, now, nowMs + 40 - 600, timing);
        rig.Entity.Target = rig.Slime.Id;
        rig.Entity.Combat.BeginSwing(rig.Slime.Id, now, nowMs + 50 - 600, timing);
        rig.Server.Tick();

        Assert.That(rig.Entity.IsDead, Is.True);
        Assert.That(rig.Slime.IsDead, Is.False, "the dead attacker's swing is interrupted");
        Assert.That(rig.Damages().Select(damage => damage.Target), Is.EqualTo(new[] { rig.Entity.Id }));
    }

    [Test]
    public void Attack_CancelOrTargetSpam_NeverBeatsTheInterval()
    {
        var rig = new CombatRig();
        rig.Slime.CurrentHealth = 1_000_000;
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        uint first = rig.Starts()[0].StartTick;

        for (int index = 0; index < 40; index++)
        {
            rig.Cancel();
            rig.Attack(rig.Slime.Id);
            rig.Server.Tick();
        }

        List<AttackStarted> starts = rig.Starts();
        Assert.That(starts.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(starts[1].StartTick - first, Is.GreaterThanOrEqualTo(19u), "940 ms from the first nominal start");
    }

    [Test]
    public void Attack_ForTwentyIntervals_KeepsTheLongRunRateOfTheInterval()
    {
        var rig = new CombatRig();
        rig.Slime.CurrentHealth = 1_000_000;
        rig.Attack(rig.Slime.Id);

        rig.TickUntilStarts(21);

        List<AttackStarted> starts = rig.Starts();
        Assert.That(starts, Has.Count.EqualTo(21));
        Assert.That(starts[20].StartTick - starts[0].StartTick, Is.EqualTo(376u), "20 × 940 ms is 376 ticks, not 380");
        AssertGapsFollowTheInterval(starts);
    }

    [Test]
    public void Attack_InRange_BeginsASwingAndDealsDamageAtImpact()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());

        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        AttackStarted start = rig.Starts().Single();
        rig.Server.Tick(ImpactTicks - 1);
        int damageBeforeImpact = rig.Damages().Count;
        rig.Server.Tick();

        Assert.That(start.Attacker, Is.EqualTo(rig.Entity.Id));
        Assert.That(start.Target, Is.EqualTo(rig.Slime.Id));
        Assert.That(start.Timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(start.Timing.Impact, Is.EqualTo(TimeSpan.FromMilliseconds(470)));
        Assert.That(start.Timing.Recovery, Is.EqualTo(TimeSpan.FromMilliseconds(235)));
        Assert.That(damageBeforeImpact, Is.Zero, "470 ms after the start is the tick 500 ms after it");
        Damage damage = rig.Damages().Single();
        Assert.That(damage.ServerTick, Is.EqualTo(start.StartTick + ImpactTicks));
        Assert.That(damage.Result, Is.EqualTo(CombatResult.Hit));
        Assert.That(damage.Amount, Is.EqualTo(13u), "2 × 7 attack, less the slime's 2 hard defense");
        Assert.That(rig.Slime.CurrentHealth, Is.EqualTo(37));
        Assert.That(damage.TargetHealthPermille, Is.EqualTo(740));
    }

    [Test]
    public void Attack_ThroughAWall_NeverBegins()
    {
        var rig = new CombatRig();
        NavigationGrid grid = rig.Map.Definition.Navigation;
        WorldPosition? west = null;
        WorldPosition? east = null;
        for (int row = 0; row < grid.Rows && west == null; row++)
        {
            for (int column = 1; column < grid.Columns - 1 && west == null; column++)
            {
                WorldPosition wall = grid.GetCellCenter(column, row);
                if (grid.GetCell(column, row).Surface == NavigationSurface.Wall
                    && grid.CanOccupy(wall.X - 0.95f, wall.Z)
                    && grid.CanOccupy(wall.X + 0.95f, wall.Z))
                {
                    grid.TrySampleHeight(wall.X - 0.95f, wall.Z, out float westHeight);
                    grid.TrySampleHeight(wall.X + 0.95f, wall.Z, out float eastHeight);
                    west = new WorldPosition(wall.X - 0.95f, westHeight, wall.Z);
                    east = new WorldPosition(wall.X + 0.95f, eastHeight, wall.Z);
                }
            }
        }

        Assert.That(west, Is.Not.Null, "the training ground has a one-cell wall");
        rig.Slime.Position = east!.Value;
        rig.Entity.Position = west!.Value;
        rig.Server.Tick();
        Assume.That(rig.Server.SessionOf(rig.Player).KnownEntities, Does.Contain(rig.Slime.Id));

        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);

        Assert.That(rig.Starts(), Is.Empty, "1.9 m apart is within reach, but not through the wall");
    }

    [Test]
    public void Attack_WhenTheAttackerDiesBeforeImpact_IsInterrupted()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);

        rig.Server.Combat.Kill(rig.Map, rig.Entity, null, rig.Server.CurrentTick);
        rig.Server.Tick(ImpactTicks + 5);

        Assert.That(rig.Damages(), Is.Empty);
        Assert.That(rig.Slime.CurrentHealth, Is.EqualTo(50));
        EntityDied death = rig.Deaths().Single();
        Assert.That(death.Entity, Is.EqualTo(rig.Entity.Id), "the owner hears of its own death");
        Assert.That(death.Source, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Attack_WhenTheTargetDespawnsBeforeImpact_IsInterrupted()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);

        rig.Map.Remove(rig.Slime);
        rig.Server.Tick(ImpactTicks + 5);

        Assert.That(rig.Damages(), Is.Empty);
        Assert.That(rig.Entity.Combat.IsSwinging, Is.False);
        Assert.That(rig.Entity.Target, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Attack_WhenTheTargetIsJustOutOfReach_WaitsUntilItIsInReach()
    {
        var rig = new CombatRig(2.05f);

        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(20);
        int startsOutOfReach = rig.Starts().Count;
        rig.StandBeside(rig.Slime, 1.95f);
        rig.Server.Tick();

        Assert.That(startsOutOfReach, Is.Zero, "range 1.5 plus the 0.5 tolerance is 2.0");
        Assert.That(rig.Starts(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Attack_WhileTheAttackerMoves_BeginsOnlyOnceItStands()
    {
        var rig = new CombatRig(0.8f);
        rig.Attack(rig.Slime.Id);

        for (int index = 0; index < 5; index++)
        {
            rig.Move(0f, index % 2 == 0 ? 1f : -1f);
            rig.Server.Tick();
        }

        int startsWhileMoving = rig.Starts().Count;
        rig.Server.Tick(8);

        Assert.That(startsWhileMoving, Is.Zero);
        Assert.That(rig.Starts(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Cancel_AfterASwingBegan_LetsItResolveButBeginsNoOther()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);

        rig.Cancel();
        rig.Server.Tick(60);

        Assert.That(rig.Damages(), Has.Count.EqualTo(1));
        Assert.That(rig.Starts(), Has.Count.EqualTo(1));
        Assert.That(rig.Entity.Target, Is.EqualTo(rig.Slime.Id), "cancel ends the attack, not the selection");
    }

    [Test]
    public void Kill_OfTheTarget_EndsTheAttackAndClearsTheTarget()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 13;
        rig.Attack(rig.Slime.Id);

        rig.TickUntilStarts(1);
        rig.Server.Tick(60);

        Assert.That(rig.Slime.IsDead, Is.True);
        Assert.That(rig.Slime.StateFlags & EntityStateFlags.Dead, Is.EqualTo(EntityStateFlags.Dead));
        EntityDied death = rig.Deaths().Single();
        Assert.That(death.Entity, Is.EqualTo(rig.Slime.Id));
        Assert.That(death.Source, Is.EqualTo(rig.Entity.Id));
        Assert.That(rig.Damages().Single().TargetHealthPermille, Is.Zero);
        Assert.That(rig.Starts(), Has.Count.EqualTo(1), "no swing at a corpse");
        Assert.That(rig.Entity.Target, Is.EqualTo(default(EntityId)));
        Assert.That(rig.Entity.Combat.IsAutoAttacking, Is.False);
        var opcodes = rig.Server.Transport.ControlOpcodesSentTo(rig.Player).ToList();
        Assert.That(opcodes.LastIndexOf(MessageOpcode.TargetChanged),
            Is.GreaterThan(opcodes.IndexOf(MessageOpcode.Damage)));
    }

    [Test]
    public void Lock_FromSwingStartToImpact_HoldsThePlayerThenReleasesIt()
    {
        var rig = new CombatRig();
        rig.Slime.CurrentHealth = 1_000_000;
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        WorldPosition atStart = rig.Entity.Position;

        var positions = new List<WorldPosition>();
        for (int index = 0; index < ImpactTicks; index++)
        {
            rig.Cancel();
            rig.Move(1f, 0f);
            rig.Server.Tick();
            positions.Add(rig.Entity.Position);
        }

        rig.Move(1f, 0f);
        rig.Server.Tick();

        Assert.That(positions, Is.All.EqualTo(atStart), "ticks S+1 to the impact tick S+10 are locked");
        Assert.That(rig.Entity.Position, Is.Not.EqualTo(atStart), "free after impact");
        Assert.That(rig.Server.SessionOf(rig.Player).Input!.LastProcessedSequence, Is.EqualTo(11u));
    }

    [Test]
    public void Monster_AttackingThePlayer_IsReportedToTheOwnerWithItsHealth()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.Target = rig.Entity.Id;
        rig.Slime.Combat.IsAutoAttacking = true;

        rig.TickUntilStarts(1);
        AttackStarted start = rig.Starts().Single();
        rig.Server.Tick(13);

        Assert.That(start.Attacker, Is.EqualTo(rig.Slime.Id));
        Assert.That(start.Target, Is.EqualTo(rig.Entity.Id));
        Assert.That(start.Timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(1200)));
        Assert.That(start.Timing.Impact, Is.EqualTo(TimeSpan.FromMilliseconds(600)));
        Damage damage = rig.Damages().Single();
        Assert.That(damage.Target, Is.EqualTo(rig.Entity.Id));
        Assert.That(damage.TargetHealthPermille, Is.Zero, "a player's HP is never shared as a ratio");
        CharacterHealth health = rig.Received(
            MessageOpcode.CharacterHealth,
            payload => CharacterHealth.TryRead(payload, out CharacterHealth m) ? m : (CharacterHealth?)null).Single();
        Assert.That(health.Current, Is.EqualTo((uint)(71 - (int)damage.Amount)));
        Assert.That(health.Maximum, Is.EqualTo(71u));
    }

    [Test]
    public void Retarget_AfterASwingBegan_LetsItResolveOnTheOriginalTarget()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        MonsterEntity other = rig.Map.Monsters.First(monster => monster != rig.Slime);
        rig.Server.Tick();
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);

        rig.Server.SendTarget(rig.Player, other.Id);
        rig.Server.Tick(40);

        Assert.That(rig.Damages().Select(damage => damage.Target), Is.EqualTo(new[] { rig.Slime.Id }));
        Assert.That(rig.Starts(), Has.Count.EqualTo(1), "selecting another target ends the attack");
    }

    [Test]
    public void Swing_BlockedPastItsNominalStart_StartsANewSchedule()
    {
        var rig = new CombatRig();
        rig.Slime.CurrentHealth = 1_000_000;
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        uint first = rig.Starts()[0].StartTick;

        // The next nominal start is 940 ms later, first reached 19 ticks on; walking then delays it.
        rig.Server.Tick(18);
        rig.Move(1f, 0f);
        rig.Server.Tick();
        rig.Move(-1f, 0f);
        rig.Server.Tick();
        rig.Move(0f, 0f);
        rig.TickUntilStarts(3);

        List<AttackStarted> starts = rig.Starts();
        Assert.That(starts[1].StartTick - first, Is.GreaterThan(19u), "the walk delayed the second swing");
        Assert.That(starts[2].StartTick - starts[1].StartTick, Is.EqualTo(19u), "940 ms from the late start");
    }
}
}
