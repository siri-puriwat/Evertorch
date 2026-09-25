using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class RenewalProgressionRulesTests
{
    // The experience research note's example table: nine entries, so the cap is level 10.
    private static readonly ExperienceTableDefinition Table = new(
        new ExperienceDefinitionId("experience.adventurer"),
        new[] { 30, 50, 80, 120, 170, 230, 300, 380, 470 });

    private readonly RenewalProgressionRules m_rules = new();

    // Research note vectors: base experience, the character's damage, the damage logged in all => its share.
    [TestCase(10, 30, 50, 6)]
    [TestCase(10, 20, 50, 4)]
    [TestCase(10, 1, 100, 1)]
    [TestCase(10, 99, 100, 9)]
    [TestCase(10, 45, 65, 6)]
    [TestCase(10, 20, 65, 3)]
    [TestCase(10, 1, 3, 3)]
    [TestCase(0, 50, 50, 0)]
    [TestCase(7, 50, 50, 7)]
    public void ShareExperience_ForResearchVector_MatchesTheShare(
        long baseExperience,
        long damage,
        long totalDamage,
        long share)
    {
        Assert.That(m_rules.ShareExperience(baseExperience, damage, totalDamage), Is.EqualTo(share));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void ShareExperience_ForNoDamage_GivesNothing(long damage)
    {
        Assert.That(m_rules.ShareExperience(10, damage, 50), Is.Zero);
    }

    // Research note vectors: level and experience before, the award => level and experience after.
    [TestCase(1, 25, 10, 2, 5)]
    [TestCase(1, 0, 100, 3, 20)]
    [TestCase(9, 460, 20, 10, 0)]
    [TestCase(10, 0, 50, 10, 0)]
    [TestCase(1, 29, 0, 1, 29)]
    [TestCase(1, 0, 30, 2, 0)]
    [TestCase(1, 0, 1_000_000, 10, 0)]
    [TestCase(12, 0, 50, 12, 0)]
    public void AddExperience_ForResearchVector_CarriesSurplusUpToTheCap(
        int level,
        long experience,
        long award,
        int expectedLevel,
        long expectedExperience)
    {
        LevelProgress after = m_rules.AddExperience(Table, new LevelProgress(level, experience), award);

        Assert.That(after.Level, Is.EqualTo(expectedLevel), "level");
        Assert.That(after.Experience, Is.EqualTo(expectedExperience), "experience");
    }

    [TestCase(1, 30)]
    [TestCase(2, 50)]
    [TestCase(9, 470)]
    [TestCase(10, 0)]
    [TestCase(11, 0)]
    public void ExperienceToNextLevel_ReadsTheTableUpToTheCap(int level, long expected)
    {
        Assert.That(m_rules.ExperienceToNextLevel(Table, level), Is.EqualTo(expected));
    }

    [Test]
    public void LevelProgress_BelowLevelOneOrWithNegativeExperience_Throws()
    {
        Action belowLevelOne = () => _ = new LevelProgress(0, 0);
        Action negative = () => _ = new LevelProgress(1, -1);

        Assert.That(belowLevelOne, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(negative, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ShareExperience_WhenTheProductPasses64Bits_StaysExact()
    {
        long share = m_rules.ShareExperience(1_000_000_000, 4_000_000_000_000_000_000, 8_000_000_000_000_000_000);

        Assert.That(share, Is.EqualTo(500_000_000));
    }
}
}
