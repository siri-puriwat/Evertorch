using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class EntityIdTests
{
    [TestCase(0L)]
    [TestCase(42L)]
    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void Value_AfterConstruction_ReturnsSuppliedValue(long value)
    {
        EntityId entityId = new EntityId(value);

        Assert.That(entityId.Value, Is.EqualTo(value));
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        EntityId left = new EntityId(7);
        EntityId right = new EntityId(7);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        EntityId left = new EntityId(7);
        EntityId right = new EntityId(8);

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenComparedWithAnotherType_IsFalse()
    {
        EntityId entityId = new EntityId(7);

        Assert.That(entityId.Equals(7L), Is.False);
    }

    [Test]
    public void ToString_ForAnyCulture_IsInvariantNumber()
    {
        Assert.That(new EntityId(-1234567).ToString(), Is.EqualTo("-1234567"));
    }
}
}
