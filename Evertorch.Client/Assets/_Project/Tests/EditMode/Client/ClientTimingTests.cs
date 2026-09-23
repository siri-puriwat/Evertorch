using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientTimingTests
{
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void FixedTickClock_WithUnusableFrameTime_DoesNothing(float deltaSeconds)
    {
        var clock = new FixedTickClock(0.05f);

        Assert.That(clock.Advance(deltaSeconds), Is.EqualTo(0));
        Assert.That(clock.Alpha, Is.EqualTo(0f));
    }

    [Test]
    public void FixedTickClock_AfterALongStall_DropsTheBacklogAndCountsIt()
    {
        var clock = new FixedTickClock(0.05f);

        int due = clock.Advance(1.01f);

        Assert.That(due, Is.EqualTo(FixedTickClock.MaxTicksPerAdvance));
        Assert.That(clock.SkippedTicks, Is.EqualTo(20 - FixedTickClock.MaxTicksPerAdvance));
    }

    [Test]
    public void FixedTickClock_TurnsFrameTimeIntoWholeTicksAndARemainder()
    {
        var clock = new FixedTickClock(0.05f);

        int first = clock.Advance(0.12f);
        float alpha = clock.Alpha;
        int second = clock.Advance(0.04f);

        Assert.That(first, Is.EqualTo(2));
        Assert.That(alpha, Is.EqualTo(0.4f).Within(1e-3f));
        Assert.That(second, Is.EqualTo(1));
        Assert.That(clock.NextTick(), Is.EqualTo(1u));
        Assert.That(clock.NextTick(), Is.EqualTo(2u));
    }

    [Test]
    public void ServerTimeEstimator_LeansTowardObservationsWithoutJumping()
    {
        var estimator = new ServerTimeEstimator();
        estimator.Observe(100.0);

        estimator.Observe(100.2);

        Assert.That(estimator.Now, Is.EqualTo(100.02).Within(1e-9));
    }

    [Test]
    public void ServerTimeEstimator_StartsAtTheFirstObservationAndRunsOnLocalTime()
    {
        var estimator = new ServerTimeEstimator();

        estimator.Advance(3.0);
        estimator.Observe(100.0);
        estimator.Advance(0.25);

        Assert.That(estimator.Now, Is.EqualTo(100.25).Within(1e-9));
    }

    [Test]
    public void ServerTimeEstimator_WhenFarOff_StartsOver()
    {
        var estimator = new ServerTimeEstimator();
        estimator.Observe(100.0);

        estimator.Observe(100.0 + ServerTimeEstimator.ResetThresholdSeconds + 0.1);

        Assert.That(estimator.Now, Is.EqualTo(100.6).Within(1e-9));
    }
}
}
