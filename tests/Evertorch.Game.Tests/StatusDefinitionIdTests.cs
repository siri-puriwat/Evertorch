using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class StatusDefinitionIdTests
{
    [TestCase("status.focus")]
    [TestCase("status.focus.lesser")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new StatusDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("status")]
    [TestCase("status.")]
    [TestCase("map.training_ground")]
    [TestCase("status.Focus")]
    [TestCase("status..focus")]
    [TestCase("status.focus.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new StatusDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = StatusDefinitionId.TryCreate(value, out StatusDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(StatusDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new StatusDefinitionId("status.focus");
        var right = new StatusDefinitionId("status.focus.lesser");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new StatusDefinitionId("status.focus");
        var right = new StatusDefinitionId(string.Concat("status.", "focus"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = StatusDefinitionId.TryCreate("status.focus", out StatusDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new StatusDefinitionId("status.focus")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        StatusDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
