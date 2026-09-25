using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class ExperienceDefinitionIdTests
{
    [TestCase("experience.adventurer")]
    [TestCase("experience.novice.first_job")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new ExperienceDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("experience")]
    [TestCase("experience.")]
    [TestCase("map.training_ground")]
    [TestCase("experience.Adventurer")]
    [TestCase("experience..adventurer")]
    [TestCase("experience.adventurer.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new ExperienceDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = ExperienceDefinitionId.TryCreate(value, out ExperienceDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(ExperienceDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new ExperienceDefinitionId("experience.adventurer");
        var right = new ExperienceDefinitionId("experience.novice.first_job");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new ExperienceDefinitionId("experience.adventurer");
        var right = new ExperienceDefinitionId(string.Concat("experience.", "adventurer"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = ExperienceDefinitionId.TryCreate("experience.adventurer", out ExperienceDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new ExperienceDefinitionId("experience.adventurer")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        ExperienceDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
