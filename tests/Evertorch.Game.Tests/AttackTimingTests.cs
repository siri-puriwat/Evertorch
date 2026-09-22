using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class AttackTimingTests
{
    [TestCase(-1, 0, 0, 0)]
    [TestCase(0, -1, 0, 0)]
    [TestCase(0, 0, -1, 0)]
    [TestCase(0, 0, 0, -1)]
    public void Constructor_WhenAnyPhaseIsNegative_Throws(int interval, int windup, int impact, int recovery)
    {
        Action create = () => _ = Create(interval, windup, impact, recovery);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(881, 440, 440, 220)]
    [TestCase(880, 441, 440, 220)]
    [TestCase(880, 440, 441, 220)]
    [TestCase(880, 440, 440, 221)]
    public void Equals_WhenAnyPhaseDiffers_IsFalse(int interval, int windup, int impact, int recovery)
    {
        AttackTiming left = Create(880, 440, 440, 220);
        AttackTiming right = Create(interval, windup, impact, recovery);

        Assert.That(left.Equals(right), Is.False);
        Assert.That(left != right, Is.True);
    }

    private static AttackTiming Create(int interval, int windup, int impact, int recovery)
    {
        return new AttackTiming(
            TimeSpan.FromMilliseconds(interval),
            TimeSpan.FromMilliseconds(windup),
            TimeSpan.FromMilliseconds(impact),
            TimeSpan.FromMilliseconds(recovery));
    }

    [Test]
    public void Constructor_WhenImpactEqualsInterval_IsAllowed()
    {
        AttackTiming timing = Create(800, 800, 800, 0);

        Assert.That(timing.Impact, Is.EqualTo(timing.Interval));
    }

    [Test]
    public void Constructor_WhenImpactFallsAfterInterval_Throws()
    {
        Action create = () => _ = Create(800, 400, 801, 0);

        Assert.That(create, Throws.ArgumentException);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        AttackTiming left = Create(880, 440, 440, 220);
        AttackTiming right = Create(880, 440, 440, 220);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Properties_AfterConstruction_ReturnSuppliedValues()
    {
        AttackTiming timing = Create(880, 440, 440, 220);

        Assert.That(timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(880)));
        Assert.That(timing.Windup, Is.EqualTo(TimeSpan.FromMilliseconds(440)));
        Assert.That(timing.Impact, Is.EqualTo(TimeSpan.FromMilliseconds(440)));
        Assert.That(timing.Recovery, Is.EqualTo(TimeSpan.FromMilliseconds(220)));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ToString_UnderCommaDecimalCulture_IsInvariant()
    {
        var timing = new AttackTiming(
            TimeSpan.FromMilliseconds(880.5),
            TimeSpan.FromMilliseconds(440),
            TimeSpan.FromMilliseconds(440),
            TimeSpan.FromMilliseconds(220));

        Assert.That(
            timing.ToString(),
            Is.EqualTo("interval 880.5 ms, windup 440 ms, impact 440 ms, recovery 220 ms"));
    }
}
}
