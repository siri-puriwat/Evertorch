using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class SkillDefinitionIdTests
{
    [TestCase("skill.novice.first_aid")]
    [TestCase("skill.basic_attack")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new SkillDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("skill")]
    [TestCase("skill.")]
    [TestCase("map.training_ground")]
    [TestCase("skill.Novice.first_aid")]
    [TestCase("skill..first_aid")]
    [TestCase("skill.first_aid.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new SkillDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = SkillDefinitionId.TryCreate(value, out SkillDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(SkillDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new SkillDefinitionId("skill.novice.first_aid");
        var right = new SkillDefinitionId("skill.basic_attack");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new SkillDefinitionId("skill.novice.first_aid");
        var right = new SkillDefinitionId(string.Concat("skill.novice.", "first_aid"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = SkillDefinitionId.TryCreate("skill.novice.first_aid", out SkillDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new SkillDefinitionId("skill.novice.first_aid")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        SkillDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
