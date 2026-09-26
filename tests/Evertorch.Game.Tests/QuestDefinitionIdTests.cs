using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class QuestDefinitionIdTests
{
    [TestCase("quest.crawler_hunt")]
    [TestCase("quest.crawler_hunt.again")]
    public void Constructor_WhenValueIsValid_KeepsValue(string value)
    {
        var id = new QuestDefinitionId(value);

        Assert.That(id.Value, Is.EqualTo(value));
        Assert.That(id.ToString(), Is.EqualTo(value));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("quest")]
    [TestCase("quest.")]
    [TestCase("map.training_ground")]
    [TestCase("quest.Crawler_Hunt")]
    [TestCase("quest..crawler_hunt")]
    [TestCase("quest.crawler_hunt.")]
    public void Constructor_WhenValueIsInvalid_Throws(string? value)
    {
        Action create = () => _ = new QuestDefinitionId(value!);

        Assert.That(create, Throws.ArgumentException);
    }

    [TestCase(null)]
    [TestCase("map.training_ground")]
    public void TryCreate_WhenValueIsInvalid_ReturnsFalseAndDefault(string? value)
    {
        bool isCreated = QuestDefinitionId.TryCreate(value, out QuestDefinitionId id);

        Assert.That(isCreated, Is.False);
        Assert.That(id, Is.EqualTo(default(QuestDefinitionId)));
    }

    [Test]
    public void Equals_WhenValuesDiffer_IsFalse()
    {
        var left = new QuestDefinitionId("quest.crawler_hunt");
        var right = new QuestDefinitionId("quest.crawler_hunt.again");

        Assert.That(left, Is.Not.EqualTo(right));
        Assert.That(left == right, Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new QuestDefinitionId("quest.crawler_hunt");
        var right = new QuestDefinitionId(string.Concat("quest.", "crawler_hunt"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void TryCreate_WhenValueIsValid_ReturnsId()
    {
        bool isCreated = QuestDefinitionId.TryCreate("quest.crawler_hunt", out QuestDefinitionId id);

        Assert.That(isCreated, Is.True);
        Assert.That(id, Is.EqualTo(new QuestDefinitionId("quest.crawler_hunt")));
    }

    [Test]
    public void Value_ForDefault_IsEmpty()
    {
        QuestDefinitionId id = default;

        Assert.That(id.Value, Is.Empty);
        Assert.That(id.ToString(), Is.Empty);
    }
}
}
