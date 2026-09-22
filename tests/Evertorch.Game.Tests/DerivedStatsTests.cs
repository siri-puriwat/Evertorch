using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class DerivedStatsTests
{
    [Test]
    public void Properties_AfterConstruction_ReturnSuppliedValues()
    {
        var stats = new DerivedStats(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12.5f, 13, 14);

        Assert.That(stats.MaxHp, Is.EqualTo(1));
        Assert.That(stats.MaxSp, Is.EqualTo(2));
        Assert.That(stats.PhysicalAttack, Is.EqualTo(3));
        Assert.That(stats.MagicalAttack, Is.EqualTo(4));
        Assert.That(stats.SoftDefense, Is.EqualTo(5));
        Assert.That(stats.SoftMagicDefense, Is.EqualTo(6));
        Assert.That(stats.Hit, Is.EqualTo(7));
        Assert.That(stats.Flee, Is.EqualTo(8));
        Assert.That(stats.Critical, Is.EqualTo(9));
        Assert.That(stats.PerfectDodge, Is.EqualTo(10));
        Assert.That(stats.AttackSpeed, Is.EqualTo(11));
        Assert.That(stats.MovementSpeed, Is.EqualTo(12.5f));
        Assert.That(stats.VariableCastPermille, Is.EqualTo(13));
        Assert.That(stats.FixedCastPermille, Is.EqualTo(14));
    }
}
}
