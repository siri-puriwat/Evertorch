using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MonsterDefinitionIdTests
{
    [TestCase("monster.training_slime")]
    [TestCase("monster.field.wolf-02")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new MonsterDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("monster")]
    [TestCase("monster.")]
    [TestCase("item.material.slime_gel")]
    [TestCase("monster.Training_Slime")]
    [TestCase("monster..training_slime")]
    [TestCase("monster.training_slime.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new MonsterDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("item.material.slime_gel")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = MonsterDefinitionId.TryCreate(value, out MonsterDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(MonsterDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new MonsterDefinitionId("monster.training_slime");
        var right = new MonsterDefinitionId("monster.field.wolf-02");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new MonsterDefinitionId("monster.training_slime");
        var right = new MonsterDefinitionId(string.Concat("monster.", "training_slime"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = MonsterDefinitionId.TryCreate("monster.training_slime", out MonsterDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new MonsterDefinitionId("monster.training_slime")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        MonsterDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
