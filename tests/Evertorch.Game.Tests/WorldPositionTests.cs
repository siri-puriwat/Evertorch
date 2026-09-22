using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class WorldPositionTests
{
    [TestCase(9f, 2f, 3f)]
    [TestCase(1f, 9f, 3f)]
    [TestCase(1f, 2f, 9f)]
    public void Equals_WhenAnyComponentDiffers_IsFalse(float x, float y, float z)
    {
        var left = new WorldPosition(1f, 2f, 3f);
        var right = new WorldPosition(x, y, z);

        Assert.That(left.Equals(right), Is.False);
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Components_AfterConstruction_ReturnSuppliedValues()
    {
        var position = new WorldPosition(1.5f, -2.25f, 300f);

        Assert.That(position.X, Is.EqualTo(1.5f));
        Assert.That(position.Y, Is.EqualTo(-2.25f));
        Assert.That(position.Z, Is.EqualTo(300f));
    }

    [Test]
    public void Equals_WhenComparedWithAnotherType_IsFalse()
    {
        var position = new WorldPosition(1f, 2f, 3f);

        Assert.That(position.Equals(new WorldDirection(1f, 3f)), Is.False);
    }

    [Test]
    public void Equals_WhenComponentIsNaN_IsReflexive()
    {
        var position = new WorldPosition(float.NaN, 0f, 0f);

        Assert.That(position.Equals(position), Is.True);
        Assert.That(position.GetHashCode(), Is.EqualTo(new WorldPosition(float.NaN, 0f, 0f).GetHashCode()));
    }

    [Test]
    public void Equals_WhenComponentsMatch_IsTrueWithMatchingHash()
    {
        var left = new WorldPosition(1f, 2f, 3f);
        var right = new WorldPosition(1f, 2f, 3f);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ToString_UnderCommaDecimalCulture_IsInvariant()
    {
        Assert.That(new WorldPosition(1.5f, -2.25f, 3f).ToString(), Is.EqualTo("(1.5, -2.25, 3)"));
    }
}
}
