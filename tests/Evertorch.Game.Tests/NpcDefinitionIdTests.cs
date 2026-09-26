using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class NpcDefinitionIdTests
{
    [TestCase("npc.quartermaster")]
    [TestCase("npc.quartermaster.east")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new NpcDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("npc")]
    [TestCase("npc.")]
    [TestCase("map.training_ground")]
    [TestCase("npc.Quartermaster")]
    [TestCase("npc..quartermaster")]
    [TestCase("npc.quartermaster.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new NpcDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = NpcDefinitionId.TryCreate(value, out NpcDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(NpcDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new NpcDefinitionId("npc.quartermaster");
        var right = new NpcDefinitionId("npc.quartermaster.east");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new NpcDefinitionId("npc.quartermaster");
        var right = new NpcDefinitionId(string.Concat("npc.", "quartermaster"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = NpcDefinitionId.TryCreate("npc.quartermaster", out NpcDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new NpcDefinitionId("npc.quartermaster")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        NpcDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
