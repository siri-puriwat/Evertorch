using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class RenewalCharacterRulesTests
{
    private const int HealthBase = 60;
    private const int HealthPerLevel = 8;
    private const int SpiritBase = 20;
    private const int SpiritPerLevel = 3;
    private const int UnarmedPenalty = 44;

    // Golden vectors computed independently with exact rational arithmetic; see the private research note.
    // level, str, agi, vit, int, dex, luk => atk, matk, sdef, smdef, hit, flee, crit, dodge, hp, sp, aspd, vcast
    [TestCase(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 177, 102, 13, 11, 68, 23, 152, 924)]
    [TestCase(10, 20, 15, 10, 5, 12, 3, 25, 12, 13, 11, 198, 125, 20, 13, 154, 52, 154, 766)]
    [TestCase(50, 60, 40, 30, 1, 30, 10, 81, 22, 48, 25, 258, 192, 45, 20, 598, 171, 159, 660)]
    [TestCase(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 176, 101, 10, 10, 68, 23, 152, 1000)]
    [TestCase(7, 9, 4, 9, 9, 4, 9, 14, 17, 8, 13, 189, 112, 37, 19, 126, 44, 152, 820)]
    [TestCase(99, 99, 99, 99, 99, 99, 99, 176, 224, 118, 163, 406, 317, 316, 109, 1695, 630, 172, 251)]
    [TestCase(175, 130, 130, 130, 130, 130, 130, 243, 307, 178, 225, 523, 431, 417, 140, 3358, 1253, 179, 142)]
    public void CalculateDerivedStats_ForGoldenVector_MatchesEveryStatistic(
        int level,
        int str,
        int agi,
        int vit,
        int @int,
        int dex,
        int luk,
        int physicalAttack,
        int magicalAttack,
        int softDefense,
        int softMagicDefense,
        int hit,
        int flee,
        int critical,
        int perfectDodge,
        int maxHp,
        int maxSp,
        int attackSpeed,
        int variableCastPermille)
    {
        DerivedStats derived = Calculate(level, new PrimaryStats(str, agi, vit, @int, dex, luk));

        Assert.That(derived.PhysicalAttack, Is.EqualTo(physicalAttack), "physical attack");
        Assert.That(derived.MagicalAttack, Is.EqualTo(magicalAttack), "magical attack");
        Assert.That(derived.SoftDefense, Is.EqualTo(softDefense), "soft defense");
        Assert.That(derived.SoftMagicDefense, Is.EqualTo(softMagicDefense), "soft magic defense");
        Assert.That(derived.Hit, Is.EqualTo(hit), "hit");
        Assert.That(derived.Flee, Is.EqualTo(flee), "flee");
        Assert.That(derived.Critical, Is.EqualTo(critical), "critical");
        Assert.That(derived.PerfectDodge, Is.EqualTo(perfectDodge), "perfect dodge");
        Assert.That(derived.MaxHp, Is.EqualTo(maxHp), "max HP");
        Assert.That(derived.MaxSp, Is.EqualTo(maxSp), "max SP");
        Assert.That(derived.AttackSpeed, Is.EqualTo(attackSpeed), "attack speed");
        Assert.That(derived.VariableCastPermille, Is.EqualTo(variableCastPermille), "variable cast");
        Assert.That(derived.FixedCastPermille, Is.EqualTo(1000), "fixed cast");
        Assert.That(derived.MovementSpeed, Is.EqualTo(5f), "movement speed");
    }

    // Each term truncates to tenths before the sum. At level 3 with dex 3 and luk 2 the terms are 7, 6, and 6
    // tenths, giving 1; summing the untruncated 7.5, 6, and 6.67 would give 2.
    [TestCase(3, 0, 3, 2, 1)]
    [TestCase(1, 0, 4, 2, 1)]
    [TestCase(1, 1, 4, 2, 2)]
    [TestCase(1, 0, 5, 0, 1)]
    [TestCase(4, 0, 0, 0, 1)]
    [TestCase(3, 0, 0, 0, 0)]
    public void CalculateDerivedStats_AtAttackRoundingBoundaries_TruncatesPerTerm(
        int level,
        int str,
        int dex,
        int luk,
        int expectedAttack)
    {
        DerivedStats derived = Calculate(level, new PrimaryStats(str, 0, 0, 0, dex, luk));

        Assert.That(derived.PhysicalAttack, Is.EqualTo(expectedAttack));
    }

    // (level + vit) / 2 and agi / 5 are summed before truncating: 0.5 + 0.6 reaches 1, separately they would not.
    [TestCase(1, 0, 3, 1)]
    [TestCase(1, 0, 2, 0)]
    [TestCase(1, 1, 0, 1)]
    [TestCase(2, 1, 4, 2)]
    public void CalculateDerivedStats_AtSoftDefenseBoundaries_TruncatesOnce(
        int level,
        int vit,
        int agi,
        int expectedSoftDefense)
    {
        DerivedStats derived = Calculate(level, new PrimaryStats(0, agi, vit, 0, 0, 0));

        Assert.That(derived.SoftDefense, Is.EqualTo(expectedSoftDefense));
    }

    // level / 4 and (dex + vit) / 5 are summed before truncating: 0.75 + 0.4 reaches 1.
    [TestCase(3, 0, 2, 0, 1)]
    [TestCase(3, 0, 1, 0, 0)]
    [TestCase(1, 7, 0, 0, 7)]
    [TestCase(4, 0, 0, 0, 1)]
    public void CalculateDerivedStats_AtSoftMagicDefenseBoundaries_TruncatesOnce(
        int level,
        int @int,
        int dex,
        int vit,
        int expectedSoftMagicDefense)
    {
        DerivedStats derived = Calculate(level, new PrimaryStats(0, 0, vit, @int, dex, 0));

        Assert.That(derived.SoftMagicDefense, Is.EqualTo(expectedSoftMagicDefense));
    }

    // sqrt(dex^2 / 5 + agi^2 / 2) / 4 first reaches 1 at agi 6 (sqrt(18) / 4 = 1.06); agi 5 gives 0.88.
    [TestCase(5, 0, 152)]
    [TestCase(6, 0, 153)]
    [TestCase(0, 8, 152)]
    [TestCase(0, 9, 153)]
    public void CalculateDerivedStats_AtAttackSpeedBoundaries_TruncatesTheSquareRootTerm(
        int agi,
        int dex,
        int expectedAttackSpeed)
    {
        DerivedStats derived = Calculate(1, new PrimaryStats(0, agi, 0, 0, dex, 0));

        Assert.That(derived.AttackSpeed, Is.EqualTo(expectedAttackSpeed));
    }

    // 2 dex + int = 530 removes the variable cast entirely. At 529 less than a thousandth remains and truncates
    // away; 528 is the first total that leaves one.
    [TestCase(265, 0, 0)]
    [TestCase(0, 530, 0)]
    [TestCase(264, 1, 0)]
    [TestCase(264, 0, 1)]
    [TestCase(9999, 9999, 0)]
    public void CalculateDerivedStats_AtVariableCastBoundaries_NeverGoesNegative(int dex, int @int, int expected)
    {
        DerivedStats derived = Calculate(1, new PrimaryStats(0, 0, 0, @int, dex, 0));

        Assert.That(derived.VariableCastPermille, Is.EqualTo(expected));
    }

    [TestCase(7, 50, 10)]
    [TestCase(1, 99, 1)]
    [TestCase(99, 150, 247)]
    [TestCase(5, 0, 5)]
    [TestCase(0, 1000, 0)]
    public void ApplyStatPercent_FloorsWhatEachStatisticGains(int stat, int percent, int expected)
    {
        var stats = new PrimaryStats(stat, stat, stat, stat, stat, stat);

        PrimaryStats changed = new RenewalCharacterRules().ApplyStatPercent(
            stats,
            new StatPercentages(percent, percent, percent, percent, percent, percent));

        Assert.That(changed, Is.EqualTo(new PrimaryStats(expected, expected, expected, expected, expected, expected)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CharacterBuild_WhenLevelIsBelowOne_Throws(int level)
    {
        Action create = () => _ = new CharacterBuild(level, default, 1, 1, 1, 1, 0, 5f);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // Equipment research note: a weapon's penalty replaces the job's unarmed 44, so with every statistic 5 the sword's
    // 50 gives 147 and the staff's 54 gives 143 (unarmed 153); the staff's INT +10 is a primary statistic like any.
    [TestCase(50, 5, 147, 24, 831, 7)]
    [TestCase(54, 15, 143, 26, 782, 17)]
    public void CalculateDerivedStats_WithAWeapon_TakesItsPenaltyAndBonus(
        int penalty,
        int @int,
        int attackSpeed,
        int maxSp,
        int variableCastPermille,
        int softMagicDefense)
    {
        var build = new CharacterBuild(
            1,
            new PrimaryStats(5, 5, 5, @int, 5, 5),
            HealthBase,
            HealthPerLevel,
            SpiritBase,
            SpiritPerLevel,
            penalty,
            5f);

        DerivedStats derived = new RenewalCharacterRules().CalculateDerivedStats(build);

        Assert.That(
            (derived.AttackSpeed, derived.MaxSp, derived.VariableCastPermille, derived.SoftMagicDefense),
            Is.EqualTo((attackSpeed, maxSp, variableCastPermille, softMagicDefense)));
    }

    private static DerivedStats Calculate(int level, PrimaryStats stats)
    {
        var build = new CharacterBuild(
            level,
            stats,
            HealthBase,
            HealthPerLevel,
            SpiritBase,
            SpiritPerLevel,
            UnarmedPenalty,
            5f);

        return new RenewalCharacterRules().CalculateDerivedStats(build);
    }

    // Research note vectors, from the golden rows above: HP vit ÷ 5 + max(1, maxHp ÷ 200) every 6 s; SP
    // 1 + int ÷ 6 + maxSp ÷ 100 every 8 s.
    // level, every primary statistic => HP per step, SP per step
    [TestCase(1, 5, 2, 1)]
    [TestCase(1, 1, 1, 1)]
    [TestCase(99, 99, 27, 23)]
    [TestCase(175, 130, 42, 43)]
    public void CalculateRegeneration_ForResearchVector_MatchesTheAmounts(int level, int stat, int health, int spirit)
    {
        var stats = new PrimaryStats(stat, stat, stat, stat, stat, stat);

        Regeneration regeneration = new RenewalCharacterRules().CalculateRegeneration(stats, Calculate(level, stats));

        Assert.That(regeneration.Health, Is.EqualTo(health), "HP");
        Assert.That(regeneration.Spirit, Is.EqualTo(spirit), "SP");
        Assert.That(regeneration.HealthIntervalMs, Is.EqualTo(6000));
        Assert.That(regeneration.SpiritIntervalMs, Is.EqualTo(8000));
    }

    // From INT 120 SP gains (int − 120) ÷ 2 + 4 more; below it, nothing.
    [TestCase(119, 0, 20)]
    [TestCase(120, 0, 25)]
    [TestCase(121, 0, 25)]
    [TestCase(122, 0, 26)]
    [TestCase(1, 199, 2)]
    [TestCase(1, 200, 3)]
    public void CalculateRegeneration_AroundTheThresholds_AddsAtEachStep(int intelligence, int maxSp, int spirit)
    {
        var stats = new PrimaryStats(1, 1, 1, intelligence, 1, 1);

        Regeneration regeneration = new RenewalCharacterRules().CalculateRegeneration(stats, WithMaximums(1, maxSp));

        Assert.That(regeneration.Spirit, Is.EqualTo(spirit));
    }

    [TestCase(0, 199, 1)]
    [TestCase(0, 400, 2)]
    [TestCase(4, 1, 1)]
    [TestCase(5, 1, 2)]
    public void CalculateRegeneration_ForHealth_NeverGivesLessThanOnePerStep(int vit, int maxHp, int health)
    {
        var stats = new PrimaryStats(1, 1, vit, 1, 1, 1);

        Regeneration regeneration = new RenewalCharacterRules().CalculateRegeneration(stats, WithMaximums(maxHp, 0));

        Assert.That(regeneration.Health, Is.EqualTo(health));
    }

    private static DerivedStats WithMaximums(int maxHp, int maxSp)
    {
        return new DerivedStats(maxHp, maxSp, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 1000, 1000);
    }

    // Research note vector: Focus adds 100 % to AGI and DEX; with every statistic 5 the attack speed goes from 153 to
    // 154 (an interval of 940 ms to 920 ms) and the hit from 182 to 187.
    [Test]
    public void ApplyStatPercent_ForFocus_DoublesAgiAndDex_AndQuickensTheAttack()
    {
        var rules = new RenewalCharacterRules();
        var stats = new PrimaryStats(5, 5, 5, 5, 5, 5);

        PrimaryStats focused = rules.ApplyStatPercent(stats, new StatPercentages(0, 100, 0, 0, 100, 0));
        DerivedStats before = Calculate(1, stats);
        DerivedStats after = Calculate(1, focused);

        Assert.That(focused, Is.EqualTo(new PrimaryStats(5, 10, 5, 5, 10, 5)));
        Assert.That((before.AttackSpeed, after.AttackSpeed), Is.EqualTo((153, 154)));
        Assert.That((before.Hit, after.Hit), Is.EqualTo((182, 187)));
    }

    [Test]
    public void CalculateDerivedStats_ForIdenticalBuilds_IsDeterministic()
    {
        DerivedStats first = Calculate(42, new PrimaryStats(31, 17, 23, 5, 29, 11));
        DerivedStats second = Calculate(42, new PrimaryStats(31, 17, 23, 5, 29, 11));

        Assert.That(second.MaxHp, Is.EqualTo(first.MaxHp));
        Assert.That(second.AttackSpeed, Is.EqualTo(first.AttackSpeed));
        Assert.That(second.VariableCastPermille, Is.EqualTo(first.VariableCastPermille));
    }

    [Test]
    public void CalculateDerivedStats_ForLargeJobValues_DoesNotOverflow()
    {
        var build = new CharacterBuild(
            999,
            new PrimaryStats(0, 0, 9999, 9999, 0, 0),
            100_000_000,
            100_000_000,
            100_000_000,
            100_000_000,
            0,
            5f);

        DerivedStats derived = new RenewalCharacterRules().CalculateDerivedStats(build);

        Assert.That(derived.MaxHp, Is.EqualTo(int.MaxValue));
        Assert.That(derived.MaxSp, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void CalculateDerivedStats_WhenPenaltyExceedsTheBase_ClampsAttackSpeedAtZero()
    {
        var build = new CharacterBuild(1, new PrimaryStats(0, 0, 0, 0, 0, 0), 1, 0, 0, 0, 500, 5f);

        DerivedStats derived = new RenewalCharacterRules().CalculateDerivedStats(build);

        Assert.That(derived.AttackSpeed, Is.Zero);
    }

    [Test]
    public void CalculateDerivedStats_WhenStatsExceedTheCap_ClampsAttackSpeed()
    {
        DerivedStats derived = Calculate(1, new PrimaryStats(0, 9999, 0, 0, 9999, 0));

        Assert.That(derived.AttackSpeed, Is.EqualTo(RenewalCharacterRules.MaxAttackSpeed));
    }

    [Test]
    public void CharacterBuild_WhenJobValueIsNegative_Throws()
    {
        Action create = () => _ = new CharacterBuild(1, default, 1, -1, 1, 1, 0, 5f);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
}
