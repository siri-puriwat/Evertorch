using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class CombatPresentationTests
{
    private static readonly EntityId Slime = new(300);
    private static readonly EntityId Local = new(100);

    // The adventurer's swing: 940 ms interval, impact at 470 ms, 235 ms of recovery.
    private static readonly AttackTiming Adventurer = Timing(940, 470, 470, 235);

    private static AttackTiming Timing(int interval, int windup, int impact, int recovery)
    {
        return new AttackTiming(
            TimeSpan.FromMilliseconds(interval),
            TimeSpan.FromMilliseconds(windup),
            TimeSpan.FromMilliseconds(impact),
            TimeSpan.FromMilliseconds(recovery));
    }

    [TestCase(940, 470, 470, 235)]
    [TestCase(1200, 600, 600, 300)]
    [TestCase(500, 450, 450, 400)]
    [TestCase(300, 300, 300, 0)]
    [TestCase(1000, 0, 0, 0)]
    public void LungeWeight_PeaksAtImpactAndIsOverByTheInterval(int interval, int windup, int impact, int recovery)
    {
        AttackTiming timing = Timing(interval, windup, impact, recovery);
        float peak = 0f;
        double peakAt = -1.0;
        for (int millisecond = 0; millisecond <= interval + 50; millisecond++)
        {
            float weight = CombatAnimation.LungeWeight(millisecond / 1000.0, timing);
            Assert.That(weight, Is.InRange(0f, 1f));
            if (weight > peak)
            {
                peak = weight;
                peakAt = millisecond;
            }

            if (millisecond >= interval && millisecond > impact)
            {
                Assert.That(weight, Is.Zero, $"still lunging at {millisecond} ms");
            }
        }

        Assert.That(peak, Is.EqualTo(1f));
        Assert.That(peakAt, Is.EqualTo(impact));
    }

    [Test]
    public void LungeWeight_RisesToOneExactlyAtImpactAndReturnsByTheEndOfRecovery()
    {
        Assert.That(CombatAnimation.LungeWeight(-0.01, Adventurer), Is.Zero);
        Assert.That(CombatAnimation.LungeWeight(0.0, Adventurer), Is.Zero);
        Assert.That(CombatAnimation.LungeWeight(0.2, Adventurer), Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(CombatAnimation.LungeWeight(0.47, Adventurer), Is.EqualTo(1f));
        Assert.That(CombatAnimation.LungeWeight(0.6, Adventurer), Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(CombatAnimation.LungeWeight(0.705, Adventurer), Is.Zero);
        Assert.That(CombatAnimation.LungeWeight(0.9, Adventurer), Is.Zero);
    }

    [Test]
    public void SquashWeight_IsFullAtImpactAndGoneAQuarterSecondLater()
    {
        Assert.That(CombatAnimation.SquashWeight(-0.01), Is.Zero);
        Assert.That(CombatAnimation.SquashWeight(0.0), Is.EqualTo(1f));
        Assert.That(CombatAnimation.SquashWeight(0.125), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(CombatAnimation.SquashWeight(CombatAnimation.SquashSeconds), Is.Zero);
    }

    [Test]
    public void Timeline_DrawsACastFromItsStartToItsEnd_OnItsCastersClock()
    {
        var timeline = new CombatTimeline();

        timeline.BeginCast(Local, default, 10.0, 1.5, true);
        timeline.BeginCast(Slime, Local, 2.0, 1.0, false);

        Assert.That(timeline.TryGetCastProgress(Local, 10.0, 0.0, out float start), Is.True);
        Assert.That(start, Is.Zero);
        Assert.That(timeline.TryGetCastProgress(Local, 10.75, 0.0, out float half), Is.True);
        Assert.That(half, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(timeline.TryGetCastProgress(Local, 11.5, 0.0, out float _), Is.False, "the cast time is over");
        Assert.That(timeline.TryGetCastProgress(Slime, 99.0, 1.9, out float _), Is.False, "not begun on its timeline");
        Assert.That(timeline.TryGetCastProgress(Slime, 0.0, 2.25, out float remote), Is.True);
        Assert.That(remote, Is.EqualTo(0.25f).Within(1e-4f));
    }

    [Test]
    public void Timeline_DrawsADeadRemoteDeadFromItsDeathOnItsTimeline()
    {
        var timeline = new CombatTimeline();

        timeline.MarkDeath(Slime, 3.0, false);

        Assert.That(timeline.IsShownDead(Slime, true, 9.0, 2.9), Is.False, "the final blow is still being drawn");
        Assert.That(timeline.IsShownDead(Slime, true, 9.0, 3.0), Is.True);
        Assert.That(timeline.IsShownDead(new EntityId(301), true, 0.0, 0.0), Is.True, "a body seen already dead");
        timeline.ClearDeath(Slime);
        Assert.That(timeline.IsShownDead(Slime, false, 9.0, 9.0), Is.False);
    }

    [Test]
    public void Timeline_EndsACast_WhenItsCasterOrTargetDiesOrLeaves_OrItIsCancelled()
    {
        var timeline = new CombatTimeline();
        var other = new EntityId(301);
        timeline.BeginCast(Local, Slime, 1.0, 2.0, true);
        timeline.BeginCast(Slime, Local, 1.0, 2.0, false);
        timeline.BeginCast(other, default, 1.0, 2.0, false);
        timeline.BeginCast(new EntityId(302), default, 1.0, 0.0, false);

        timeline.EndCastsOf(Slime);
        bool isOtherKept = timeline.TryGetCastProgress(other, 1.5, 1.5, out float _);
        timeline.EndCast(other);

        Assert.That(timeline.TryGetCastProgress(Local, 1.5, 1.5, out float _), Is.False, "its target died");
        Assert.That(timeline.TryGetCastProgress(Slime, 1.5, 1.5, out float _), Is.False, "its caster died");
        Assert.That(isOtherKept, Is.True);
        Assert.That(timeline.TryGetCastProgress(other, 1.5, 1.5, out float _), Is.False, "cancelled");
        Assert.That(timeline.TryGetCastProgress(new EntityId(302), 1.0, 1.0, out float _), Is.False, "no cast time");
    }

    [Test]
    public void Timeline_ForgetsEverythingAboutADespawnedEntity()
    {
        var timeline = new CombatTimeline();
        var due = new List<HitMark>();
        timeline.BeginSwing(Slime, 1.0, Adventurer, false);
        timeline.AddHit(new HitMark(Slime, 1.47, false, CombatResult.Critical, 30, 100));

        timeline.Forget(Slime);
        timeline.CollectDueHits(9.0, 9.0, due);

        Assert.That(due, Is.Empty);
        Assert.That(timeline.Lunge(Slime, 1.47, 1.47), Is.Zero);
        Assert.That(timeline.Squash(Slime, 1.47, 1.47), Is.Zero);
    }

    [Test]
    public void Timeline_KeepsTheLocalSwingOnTheLocalClock()
    {
        var timeline = new CombatTimeline();

        timeline.BeginSwing(Local, 10.0, Adventurer, true);

        Assert.That(timeline.Lunge(Local, 10.47, 0.0), Is.EqualTo(1f).Within(1e-4f));
        Assert.That(timeline.Lunge(Local, 10.0, 99.0), Is.Zero);
        Assert.That(timeline.Lunge(Slime, 10.47, 10.47), Is.Zero, "no swing, no lunge");
    }

    [Test]
    public void Timeline_ShowsAHealWithoutASquash()
    {
        var timeline = new CombatTimeline();
        var due = new List<HitMark>();

        timeline.AddHit(new HitMark(Local, 1.0, true, CombatResult.Hit, 15, 0, true));
        timeline.CollectDueHits(1.0, 0.0, due);

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].IsHeal, Is.True);
        Assert.That(timeline.Squash(Local, 1.0, 0.0), Is.Zero);
    }

    [Test]
    public void Timeline_ShowsAMissWithoutASquash()
    {
        var timeline = new CombatTimeline();
        var due = new List<HitMark>();

        timeline.AddHit(new HitMark(Slime, 1.0, false, CombatResult.Miss, 0, 1000));
        timeline.CollectDueHits(0.0, 1.0, due);

        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(timeline.Squash(Slime, 0.0, 1.0), Is.Zero);
    }

    [Test]
    public void Timeline_ShowsARemoteHitOnlyWhenTheInterpolatedTimeReachesIt()
    {
        var timeline = new CombatTimeline();
        var due = new List<HitMark>();
        timeline.AddHit(new HitMark(Slime, 2.0, false, CombatResult.Hit, 12, 760));

        timeline.CollectDueHits(5.0, 1.99, due);
        int beforeItsTime = due.Count;
        timeline.CollectDueHits(5.0, 2.0, due);

        Assert.That(beforeItsTime, Is.Zero, "the local clock does not move a remote hit");
        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].Amount, Is.EqualTo(12u));
        Assert.That(timeline.PendingHits, Is.Zero);
        Assert.That(timeline.Squash(Slime, 5.0, 2.0), Is.EqualTo(1f));
    }
}
}
