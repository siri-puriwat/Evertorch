using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class CharacterIdTests
{
    [TestCase(0L)]
    [TestCase(42L)]
    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void Value_AfterConstruction_ReturnsSuppliedValue(long value)
    {
        var characterId = new CharacterId(value);

        Assert.That(characterId.Value, Is.EqualTo(value));
    }

    [Test]
    public void Equals_WhenComparedWithEntityId_IsFalse()
    {
        var characterId = new CharacterId(7);

        Assert.That(characterId.Equals(new EntityId(7)), Is.False);
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new CharacterId(7);
        var right = new CharacterId(8);

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new CharacterId(7);
        var right = new CharacterId(7);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void ToString_ForAnyCulture_IsInvariantNumber()
    {
        Assert.That(new CharacterId(-1234567).ToString(), Is.EqualTo("-1234567"));
    }
}
}
