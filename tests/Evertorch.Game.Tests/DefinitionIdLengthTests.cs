using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class DefinitionIdLengthTests
{
    [TestCase(64, true)]
    [TestCase(65, false)]
    [TestCase(500, false)]
    public void TryCreate_ForEveryKind_AcceptsUpToTheMaximumLength(int length, bool expected)
    {
        Assert.That(ItemDefinitionId.TryCreate(OfLength("item", length), out _), Is.EqualTo(expected));
        Assert.That(MonsterDefinitionId.TryCreate(OfLength("monster", length), out _), Is.EqualTo(expected));
        Assert.That(SkillDefinitionId.TryCreate(OfLength("skill", length), out _), Is.EqualTo(expected));
        Assert.That(JobDefinitionId.TryCreate(OfLength("job", length), out _), Is.EqualTo(expected));
        Assert.That(MapDefinitionId.TryCreate(OfLength("map", length), out _), Is.EqualTo(expected));
        Assert.That(ExperienceDefinitionId.TryCreate(OfLength("experience", length), out _), Is.EqualTo(expected));
    }

    private static string OfLength(string kindPrefix, int length)
    {
        return $"{kindPrefix}.{new string('a', length - kindPrefix.Length - 1)}";
    }

    [Test]
    public void MaxLength_IsSixtyFour()
    {
        Assert.That(DefinitionIdLimits.MaxLength, Is.EqualTo(64));
    }
}
}
