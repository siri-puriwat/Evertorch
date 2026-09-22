using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MoveIntentTests
{
    [TestCase(9u, 2u, 1f, 0f)]
    [TestCase(1u, 9u, 1f, 0f)]
    [TestCase(1u, 2u, 0f, 0f)]
    [TestCase(1u, 2u, 1f, 1f)]
    public void Equals_WhenAnyValueDiffers_IsFalse(uint sequence, uint clientTick, float directionX, float directionZ)
    {
        var left = new MoveIntent(1u, 2u, 1f, 0f);
        var right = new MoveIntent(sequence, clientTick, directionX, directionZ);

        Assert.That(left.Equals(right), Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenDirectionIsNaN_IsReflexive()
    {
        var intent = new MoveIntent(1u, 2u, float.NaN, 0f);

        Assert.That(intent.Equals(intent), Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new MoveIntent(1u, 2u, 1f, 0f);
        var right = new MoveIntent(1u, 2u, 1f, 0f);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Properties_AfterConstruction_ReturnSuppliedValues()
    {
        var intent = new MoveIntent(uint.MaxValue, 12u, 0.6f, -0.8f);

        Assert.That(intent.Sequence, Is.EqualTo(uint.MaxValue));
        Assert.That(intent.ClientTick, Is.EqualTo(12u));
        Assert.That(intent.DirectionX, Is.EqualTo(0.6f));
        Assert.That(intent.DirectionZ, Is.EqualTo(-0.8f));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ToString_UnderCommaDecimalCulture_IsInvariant()
    {
        Assert.That(new MoveIntent(7u, 3u, 0.5f, -0.25f).ToString(), Is.EqualTo("#7 tick 3 (0.5, -0.25)"));
    }
}
}
