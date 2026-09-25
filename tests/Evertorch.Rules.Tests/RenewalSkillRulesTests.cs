using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class RenewalSkillRulesTests
{
    private static readonly SkillDefinition Strike = Skill(
        SkillTargetType.Enemy,
        SkillDamageType.Physical,
        0,
        0,
        SkillEffect.Damage(130));

    private static readonly SkillDefinition FirstAid = Skill(SkillTargetType.Self, null, 500, 1000,
        SkillEffect.Heal(15));

    private readonly RenewalSkillRules m_rules = new();

    private static SkillDefinition Skill(
        SkillTargetType targetType,
        SkillDamageType? damageType,
        int fixedCastMs,
        int variableCastMs,
        SkillEffect effect)
    {
        return new SkillDefinition(
            new SkillDefinitionId("skill.a"),
            "A",
            targetType,
            damageType,
            1.5,
            8,
            SkillPaymentPoint.Resolution,
            fixedCastMs,
            variableCastMs,
            500,
            2000,
            effect);
    }

    // The level 1 adventurer, every statistic 5, against the training slime (hard DEF 2, flee 102).
    private static SkillContext StrikeOnTheSlime(IRandomSource random)
    {
        return new SkillContext(
            Strike,
            AttackerKind.Character,
            831,
            new HitContext(182, 25, 102, 0, 0, random),
            new DamageContext(AttackerKind.Character, 7, 0, 2, 0, false, random));
    }

    // Research note vectors: fixed and variable cast time and the caster's share => the cast time.
    [TestCase(500, 1000, 831, 1331)]
    [TestCase(0, 750, 831, 623)]
    [TestCase(500, 1000, 251, 751)]
    [TestCase(500, 1000, 0, 500)]
    public void CalculateCastTiming_ForACharacter_ShortensOnlyTheVariablePart(
        int fixedMs,
        int variableMs,
        int permille,
        int castMs)
    {
        SkillDefinition skill = Skill(SkillTargetType.Self, null, fixedMs, variableMs, SkillEffect.Heal(15));

        CastTiming timing = m_rules.CalculateCastTiming(new SkillContext(skill, AttackerKind.Character, permille));

        Assert.That(timing.CastMs, Is.EqualTo(castMs));
        Assert.That(timing.AfterCastDelayMs, Is.EqualTo(500));
        Assert.That(timing.CooldownMs, Is.EqualTo(2000));
    }

    private sealed class FixedRandom : IRandomSource
    {
        private readonly int m_value;

        public FixedRandom(int value)
        {
            m_value = value;
        }

        public int Draws { get; private set; }

        public int Next(int exclusiveMax)
        {
            Draws++;
            return m_value;
        }
    }

    private sealed class HighestRandom : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax - 1;
        }
    }

    [Test]
    public void CalculateCastTiming_ForAMonster_TakesTheWholeCastTime()
    {
        CastTiming timing = m_rules.CalculateCastTiming(new SkillContext(FirstAid, AttackerKind.Monster, 251));

        Assert.That(timing.CastMs, Is.EqualTo(1500));
    }

    [Test]
    public void Resolve_ADamageSkillWithoutItsInputs_Throws()
    {
        Action resolve = () => m_rules.Resolve(new SkillContext(Strike, AttackerKind.Character, 831));

        Assert.That(resolve, Throws.ArgumentException);
    }

    [Test]
    public void Resolve_FirstAid_HealsItsNominalAmount_WithoutARoll()
    {
        SkillResolution resolution = m_rules.Resolve(new SkillContext(FirstAid, AttackerKind.Character, 831));

        Assert.That(resolution.Result, Is.EqualTo(SkillResult.Healed));
        Assert.That(resolution.Amount, Is.EqualTo(15));
    }

    [Test]
    public void Resolve_Strike_Deals130PercentOfTheRawDamageBeforeDefense()
    {
        SkillResolution resolution = m_rules.Resolve(StrikeOnTheSlime(new FixedRandom(0)));

        Assert.That(resolution.Result, Is.EqualTo(SkillResult.Hit));
        Assert.That(resolution.Amount, Is.EqualTo(17), "raw 14 × 130 % = 18; 18 × 4002 ÷ 4020 = 17");
    }

    [Test]
    public void Resolve_Strike_NeverCrits_AndDrawsWhatABasicAttackDraws()
    {
        var random = new FixedRandom(0);

        SkillResolution resolution = m_rules.Resolve(StrikeOnTheSlime(random));

        Assert.That(resolution.Result, Is.EqualTo(SkillResult.Hit), "a critical draw of 0 still hits normally");
        Assert.That(random.Draws, Is.EqualTo(3), "perfect dodge, critical, and hit");
    }

    [Test]
    public void Resolve_Strike_ThatMisses_DealsNothing()
    {
        SkillResolution resolution = m_rules.Resolve(StrikeOnTheSlime(new HighestRandom()));

        Assert.That(resolution.Result, Is.EqualTo(SkillResult.Miss));
        Assert.That(resolution.Amount, Is.Zero);
    }
}
}
