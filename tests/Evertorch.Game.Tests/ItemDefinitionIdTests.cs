using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class ItemDefinitionIdTests
{
    [TestCase("item.material.slime_gel")]
    [TestCase("item.consumable.minor_health")]
    [TestCase("item.x")]
    [TestCase("item.tier-2.potion_01")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        ItemDefinitionId id = new ItemDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("item")]
    [TestCase("item.")]
    [TestCase("items.slime_gel")]
    [TestCase("monster.training_slime")]
    [TestCase("Item.slime_gel")]
    [TestCase("item.Slime_Gel")]
    [TestCase("item.slime gel")]
    [TestCase("item.slime_gél")]
    [TestCase(".item.slime_gel")]
    [TestCase("item.slime_gel.")]
    [TestCase("item..slime_gel")]
    [TestCase("item.slime/gel")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new ItemDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = ItemDefinitionId.TryCreate("item.material.slime_gel", out ItemDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new ItemDefinitionId("item.material.slime_gel")));
    }

    [TestCase(null)]
    [TestCase("monster.training_slime")]
    [TestCase("item..slime_gel")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = ItemDefinitionId.TryCreate(value, out ItemDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(ItemDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        ItemDefinitionId left = new ItemDefinitionId("item.material.slime_gel");
        ItemDefinitionId right = new ItemDefinitionId(string.Concat("item.material.", "slime_gel"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        ItemDefinitionId left = new ItemDefinitionId("item.material.slime_gel");
        ItemDefinitionId right = new ItemDefinitionId("item.consumable.minor_health");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        ItemDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
        Assert.That(id.GetHashCode(), Is.EqualTo(default(ItemDefinitionId).GetHashCode()));
    }
}
}
