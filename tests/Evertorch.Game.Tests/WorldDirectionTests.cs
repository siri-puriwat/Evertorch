using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class WorldDirectionTests
{
    [TestCase(0f, 0f)]
    [TestCase(1f, 1f)]
    public void Equals_WhenAnyComponentDiffers_IsFalse(float x, float z)
    {
        var left = new WorldDirection(1f, 0f);
        var right = new WorldDirection(x, z);

        Assert.That(left.Equals(right), Is.False);
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Components_AfterConstruction_ReturnSuppliedValues()
    {
        var direction = new WorldDirection(0.6f, -0.8f);

        Assert.That(direction.X, Is.EqualTo(0.6f));
        Assert.That(direction.Z, Is.EqualTo(-0.8f));
    }

    [Test]
    public void Components_WhenNotUnitLength_AreKeptUnchanged()
    {
        var direction = new WorldDirection(3f, 4f);

        Assert.That(direction.X, Is.EqualTo(3f));
        Assert.That(direction.Z, Is.EqualTo(4f));
    }

    [Test]
    public void Equals_WhenComponentIsNaN_IsReflexive()
    {
        var direction = new WorldDirection(float.NaN, 0f);

        Assert.That(direction.Equals(direction), Is.True);
    }

    [Test]
    public void Equals_WhenComponentsMatch_IsTrueWithMatchingHash()
    {
        var left = new WorldDirection(1f, 0f);
        var right = new WorldDirection(1f, 0f);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ToString_UnderCommaDecimalCulture_IsInvariant()
    {
        Assert.That(new WorldDirection(0.5f, -0.25f).ToString(), Is.EqualTo("(0.5, -0.25)"));
    }

    [Test]
    public void Value_ForDefault_IsZero()
    {
        WorldDirection direction = default;

        Assert.That(direction, Is.EqualTo(new WorldDirection(0f, 0f)));
    }
}
}
