using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class PrimaryStatsTests
{
    [TestCase(-1, 0, 0, 0, 0, 0)]
    [TestCase(0, -1, 0, 0, 0, 0)]
    [TestCase(0, 0, -1, 0, 0, 0)]
    [TestCase(0, 0, 0, -1, 0, 0)]
    [TestCase(0, 0, 0, 0, -1, 0)]
    [TestCase(0, 0, 0, 0, 0, -1)]
    public void Constructor_WhenAnyStatIsNegative_Throws(int str, int agi, int vit, int @int, int dex, int luk)
    {
        Action create = () => _ = new PrimaryStats(str, agi, vit, @int, dex, luk);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(9, 2, 3, 4, 5, 6)]
    [TestCase(1, 9, 3, 4, 5, 6)]
    [TestCase(1, 2, 9, 4, 5, 6)]
    [TestCase(1, 2, 3, 9, 5, 6)]
    [TestCase(1, 2, 3, 4, 9, 6)]
    [TestCase(1, 2, 3, 4, 5, 9)]
    public void Equals_WhenAnyStatDiffers_IsFalse(int str, int agi, int vit, int @int, int dex, int luk)
    {
        var left = new PrimaryStats(1, 2, 3, 4, 5, 6);
        var right = new PrimaryStats(str, agi, vit, @int, dex, luk);

        Assert.That(left.Equals(right), Is.False);
        Assert.That(left != right, Is.True);
    }

    [Test]
    public void Constructor_WhenAllStatsAreZero_IsAllowed()
    {
        var stats = new PrimaryStats(0, 0, 0, 0, 0, 0);

        Assert.That(stats, Is.EqualTo(default(PrimaryStats)));
    }

    [Test]
    public void Equals_WhenValuesMatch_IsTrueWithMatchingHash()
    {
        var left = new PrimaryStats(1, 2, 3, 4, 5, 6);
        var right = new PrimaryStats(1, 2, 3, 4, 5, 6);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Properties_AfterConstruction_ReturnSuppliedValues()
    {
        var stats = new PrimaryStats(1, 2, 3, 4, 5, 6);

        Assert.That(stats.Str, Is.EqualTo(1));
        Assert.That(stats.Agi, Is.EqualTo(2));
        Assert.That(stats.Vit, Is.EqualTo(3));
        Assert.That(stats.Int, Is.EqualTo(4));
        Assert.That(stats.Dex, Is.EqualTo(5));
        Assert.That(stats.Luk, Is.EqualTo(6));
    }

    [Test]
    public void ToString_ForAnyCulture_ListsStatsInOrder()
    {
        Assert.That(
            new PrimaryStats(1, 2, 3, 4, 5, 6).ToString(),
            Is.EqualTo("STR 1 AGI 2 VIT 3 INT 4 DEX 5 LUK 6"));
    }
}
}
