using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class JobDefinitionIdTests
{
    [TestCase("job.adventurer")]
    [TestCase("job.tier-2.blade_01")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new JobDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("job")]
    [TestCase("job.")]
    [TestCase("jobs.adventurer")]
    [TestCase("map.training_ground")]
    [TestCase("job.Adventurer")]
    [TestCase("job..adventurer")]
    [TestCase("job.adventurer.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new JobDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = JobDefinitionId.TryCreate(value, out JobDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(JobDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new JobDefinitionId("job.adventurer");
        var right = new JobDefinitionId("job.tier-2.blade_01");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new JobDefinitionId("job.adventurer");
        var right = new JobDefinitionId(string.Concat("job.", "adventurer"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = JobDefinitionId.TryCreate("job.adventurer", out JobDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new JobDefinitionId("job.adventurer")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        JobDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
