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

    // Each base level L from 2 grants 3 + floor(L / 5) stat points (Gameplay Systems §2).
    [TestCase(1, 0)]
    [TestCase(2, 3)]
    [TestCase(4, 3)]
    [TestCase(5, 4)]
    [TestCase(9, 4)]
    [TestCase(10, 5)]
    [TestCase(14, 5)]
    [TestCase(15, 6)]
    public void StatPointsForLevel_ForBaseLevel_GrantsThreePlusAFifth(int level, int points)
    {
        Assert.That(m_rules.StatPointsForLevel(level), Is.EqualTo(points));
    }

    // In all: 9 by level 4, 29 by level 9, 54 by level 14, and 60 at the cap of 15.
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(2, 3)]
    [TestCase(4, 9)]
    [TestCase(5, 13)]
    [TestCase(9, 29)]
    [TestCase(10, 34)]
    [TestCase(14, 54)]
    [TestCase(15, 60)]
    public void StatPointsGranted_UpToBaseLevel_SumsEveryLevelFromTwo(int level, int points)
    {
        Assert.That(m_rules.StatPointsGranted(level), Is.EqualTo(points));
    }

    // Raising a statistic from x costs 2 + floor((x - 1) / 10).
    [TestCase(0, 2)]
    [TestCase(1, 2)]
    [TestCase(5, 2)]
    [TestCase(10, 2)]
    [TestCase(11, 3)]
    [TestCase(20, 3)]
    [TestCase(21, 4)]
    [TestCase(98, 11)]
    public void StatRaiseCost_FromValue_GrowsEveryTenPoints(int value, int cost)
    {
        Assert.That(m_rules.StatRaiseCost(value), Is.EqualTo(cost));
    }

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(6, 5)]
    [TestCase(10, 9)]
    public void SkillPointsGranted_UpToJobLevel_GrantsOnePerLevelFromTwo(int jobLevel, int points)
    {
        Assert.That(m_rules.SkillPointsGranted(jobLevel, 0), Is.EqualTo(points));
    }

    // The research note's vectors (research/jobs-and-job-change.md §4): a first job of the Adventurer, whose job cap is
    // 10, carries 9 points.
    [TestCase(1, 9)]
    [TestCase(4, 12)]
    [TestCase(10, 18)]
    public void SkillPointsGranted_ForAFirstJob_AddsThePointsItCarries(int jobLevel, int points)
    {
        Assert.That(m_rules.SkillPointsGranted(jobLevel, 9), Is.EqualTo(points));
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

    [Test]
    public void StatCap_Is99()
    {
        Assert.That(m_rules.StatCap, Is.EqualTo(99));
    }

    // A stored level no content reaches still answers at once, the sum held to what an int carries (M9 review).
    [Test]
    public void StatPointsGranted_AtAnyLevel_IsTheSumAtOnce_HeldToAnInt()
    {
        int summed = 0;
        for (int level = 2; level <= 1000; level++)
        {
            summed += m_rules.StatPointsForLevel(level);
        }

        Assert.That(m_rules.StatPointsGranted(1000), Is.EqualTo(summed));
        Assert.That(m_rules.StatPointsGranted(int.MaxValue), Is.EqualTo(int.MaxValue));
        Assert.That(m_rules.StatPointsGranted(int.MinValue), Is.Zero);
    }
}
}
