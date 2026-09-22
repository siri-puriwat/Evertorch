using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MapDefinitionIdTests
{
    [TestCase("map.training_ground")]
    [TestCase("map.town.east-gate")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new MapDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("map")]
    [TestCase("map.")]
    [TestCase("maps.training_ground")]
    [TestCase("skill.novice.first_aid")]
    [TestCase("map.Training_Ground")]
    [TestCase("map..training_ground")]
    [TestCase("map.training_ground.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new MapDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("skill.novice.first_aid")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = MapDefinitionId.TryCreate(value, out MapDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(MapDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new MapDefinitionId("map.training_ground");
        var right = new MapDefinitionId("map.town.east-gate");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new MapDefinitionId("map.training_ground");
        var right = new MapDefinitionId(string.Concat("map.", "training_ground"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = MapDefinitionId.TryCreate("map.training_ground", out MapDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new MapDefinitionId("map.training_ground")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        MapDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
