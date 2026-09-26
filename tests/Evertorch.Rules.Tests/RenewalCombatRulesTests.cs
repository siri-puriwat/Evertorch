using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
[TestFixture]
public sealed class RenewalCombatRulesTests
{
    private static readonly RenewalCombatRules Rules = new();

    // attack speed => interval, windup and impact, recovery (ms)
    [TestCase(152, 960, 480, 240)]
    [TestCase(154, 920, 460, 230)]
    [TestCase(0, 4000, 2000, 1000)]
    [TestCase(190, 200, 100, 50)]
    [TestCase(500, 200, 100, 50)]
    public void CalculateAttackTiming_ForAttackSpeed_DerivesEveryPhase(
        int attackSpeed,
        int intervalMs,
        int impactMs,
        int recoveryMs)
    {
        AttackTiming timing = Rules.CalculateAttackTiming(AttackContext.ForAttackSpeed(attackSpeed));

        Assert.That(timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(intervalMs)));
        Assert.That(timing.Windup, Is.EqualTo(TimeSpan.FromMilliseconds(impactMs)));
        Assert.That(timing.Impact, Is.EqualTo(TimeSpan.FromMilliseconds(impactMs)));
        Assert.That(timing.Recovery, Is.EqualTo(TimeSpan.FromMilliseconds(recoveryMs)));
    }

    [TestCase(1200, 600, 300)]
    [TestCase(1000, 500, 250)]
    public void CalculateAttackTiming_ForFixedInterval_SplitsTheDefinedInterval(
        int intervalMs,
        int impactMs,
        int recoveryMs)
    {
        AttackTiming timing = Rules.CalculateAttackTiming(
            AttackContext.ForFixedInterval(TimeSpan.FromMilliseconds(intervalMs)));

        Assert.That(timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(intervalMs)));
        Assert.That(timing.Windup, Is.EqualTo(TimeSpan.FromMilliseconds(impactMs)));
        Assert.That(timing.Impact, Is.EqualTo(TimeSpan.FromMilliseconds(impactMs)));
        Assert.That(timing.Recovery, Is.EqualTo(TimeSpan.FromMilliseconds(recoveryMs)));
    }

    [TestCase(10, HitOutcome.PerfectDodge, 1)]
    [TestCase(11, HitOutcome.Hit, 3)]
    public void CalculateHit_AtThePerfectDodgeBoundary_ResolvesBeforeAnythingElse(
        int dodgeRoll,
        HitOutcome expected,
        int expectedDraws)
    {
        var random = new ScriptedRandomSource(dodgeRoll, 999, 0);

        HitResult result = Rules.CalculateHit(new HitContext(177, 13, 102, 11, 0, random));

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(random.RequestedBounds, Has.Count.EqualTo(expectedDraws));
    }

    // Critical 13 against luk 5 leaves 3 in a thousand.
    [TestCase(2, HitOutcome.Critical, 2)]
    [TestCase(3, HitOutcome.Hit, 3)]
    public void CalculateHit_AtTheCriticalBoundary_SubtractsTwicePerDefenderLuk(
        int criticalRoll,
        HitOutcome expected,
        int expectedDraws)
    {
        var random = new ScriptedRandomSource(999, criticalRoll, 0);

        HitResult result = Rules.CalculateHit(new HitContext(177, 13, 102, 0, 5, random));

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(random.RequestedBounds, Has.Count.EqualTo(expectedDraws));
    }

    // hit 177 against flee 102 is 75 percent.
    [TestCase(177, 102, 74, HitOutcome.Hit, 75)]
    [TestCase(177, 102, 75, HitOutcome.Miss, 75)]
    [TestCase(100, 300, 4, HitOutcome.Hit, 5)]
    [TestCase(100, 300, 5, HitOutcome.Miss, 5)]
    [TestCase(400, 100, 99, HitOutcome.Hit, 100)]
    [TestCase(155, 106, 48, HitOutcome.Hit, 49)]
    public void CalculateHit_AtTheHitBoundary_UsesTheClampedDifference(
        int hit,
        int flee,
        int hitRoll,
        HitOutcome expected,
        int expectedChance)
    {
        var random = new ScriptedRandomSource(999, 999, hitRoll);

        HitResult result = Rules.CalculateHit(new HitContext(hit, 0, flee, 0, 0, random));

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.HitChancePercent, Is.EqualTo(expectedChance));
        Assert.That(result.DealsDamage, Is.EqualTo(expected == HitOutcome.Hit));
        Assert.That(random.RequestedBounds, Is.EqualTo(new[] { 1000, 1000, 100 }));
    }

    // Skills research note: magic attack, hard and soft magic defense, and the draw of the roll => damage. The roll is
    // 80 % of the magic attack plus the draw: 16 and 24 for 20, 20 with a draw of 4, and 5 for 5 with a draw of 1.
    [TestCase(20, 0, 7, 0, 9)]
    [TestCase(20, 0, 7, 8, 17)]
    [TestCase(20, 10, 7, 4, 11)]
    [TestCase(5, 0, 30, 1, 1)]
    public void CalculateMagicDamage_RollsTheMagicAttack_ThenAppliesMagicDefense_NeverCritical(
        int magicAttack,
        int hardMagicDefense,
        int softMagicDefense,
        int draw,
        int expected)
    {
        var random = new ScriptedRandomSource(draw);

        DamageResult result = Rules.CalculateMagicDamage(
            new MagicDamageContext(magicAttack, hardMagicDefense, softMagicDefense, random));

        Assert.That((result.Amount, result.IsCritical), Is.EqualTo((expected, false)));
        Assert.That(
            random.RequestedBounds,
            Is.EqualTo(new[] { magicAttack * 120 / 100 - magicAttack * 80 / 100 + 1 }),
            "one draw between 80 % and 120 %, both included");
    }

    // status attack, weapon attack, hard defense, soft defense => damage
    [TestCase(25, 0, 2, 0, 49)]
    [TestCase(1, 0, 2, 0, 1)]
    [TestCase(25, 10, 0, 0, 60)]
    [TestCase(500, 0, 100, 0, 820)]
    [TestCase(500, 0, 100, 20, 800)]
    [TestCase(1, 0, 0, 10, 1)]
    [TestCase(0, 0, 0, 0, 1)]
    public void CalculateDamage_ForCharacter_DoublesStatusAttackThenAppliesDefense(
        int statusAttack,
        int weaponAttack,
        int hardDefense,
        int softDefense,
        int expected)
    {
        var random = new ScriptedRandomSource();

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                statusAttack,
                weaponAttack,
                hardDefense,
                softDefense,
                false,
                random));

        Assert.That(result.Amount, Is.EqualTo(expected));
        Assert.That(result.IsCritical, Is.False);
        Assert.That(random.RequestedBounds, Is.Empty);
    }

    [TestCase(25, 2, 0, 68)]
    [TestCase(1, 2, 0, 1)]
    [TestCase(1, 0, 10, 1)]
    [TestCase(5, 0, 0, 14)]
    public void CalculateDamage_ForCriticalCharacter_MultipliesAfterDefenseAndMinimum(
        int statusAttack,
        int hardDefense,
        int softDefense,
        int expected)
    {
        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                statusAttack,
                0,
                hardDefense,
                softDefense,
                true,
                new ScriptedRandomSource()));

        Assert.That(result.Amount, Is.EqualTo(expected));
        Assert.That(result.IsCritical, Is.True);
    }

    // Attack 7 rolls 5 to 8 inclusive: four outcomes.
    [TestCase(0, 5)]
    [TestCase(3, 8)]
    public void CalculateDamage_ForMonster_RollsWithinTwentyPercentInclusive(int roll, int expected)
    {
        var random = new ScriptedRandomSource(roll);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 7, 0, 0, false, random));

        Assert.That(result.Amount, Is.EqualTo(expected));
        Assert.That(random.RequestedBounds, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void AttackContext_WhenInputsAreInvalid_Throws()
    {
        Action negativeSpeed = () => _ = AttackContext.ForAttackSpeed(-1);
        Action zeroInterval = () => _ = AttackContext.ForFixedInterval(TimeSpan.Zero);

        Assert.That(negativeSpeed, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(zeroInterval, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void CalculateDamage_ForCriticalMonster_TakesTheMaximumWithoutRolling()
    {
        var random = new ScriptedRandomSource();

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 7, 0, 3, true, random));

        Assert.That(result.Amount, Is.EqualTo(7));
        Assert.That(random.RequestedBounds, Is.Empty);
    }

    [Test]
    public void CalculateDamage_ForExtremeInputs_DoesNotOverflow()
    {
        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                int.MaxValue,
                int.MaxValue,
                0,
                0,
                true,
                new ScriptedRandomSource()));

        Assert.That(result.Amount, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void CalculateDamage_ForMonster_SubtractsSoftDefenseAndKeepsOne()
    {
        DamageResult reduced = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 7, 0, 3, false, new ScriptedRandomSource(3)));
        DamageResult floored = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 7, 0, 50, false, new ScriptedRandomSource(0)));

        Assert.That(reduced.Amount, Is.EqualTo(5));
        Assert.That(floored.Amount, Is.EqualTo(1));
    }

    [Test]
    public void CalculateHit_WhenCritical_IgnoresFlee()
    {
        var random = new ScriptedRandomSource(999, 0);

        HitResult result = Rules.CalculateHit(new HitContext(0, 500, 9999, 0, 0, random));

        Assert.That(result.Outcome, Is.EqualTo(HitOutcome.Critical));
        Assert.That(result.DealsDamage, Is.True);
    }

    [Test]
    public void CalculateHit_WhenDefenderLukCancelsCritical_NeverCrits()
    {
        var random = new ScriptedRandomSource(999, 0, 0);

        HitResult result = Rules.CalculateHit(new HitContext(177, 13, 102, 0, 7, random));

        Assert.That(result.Outcome, Is.EqualTo(HitOutcome.Hit));
    }

    [Test]
    public void Contexts_WhenInputsAreInvalid_Throw()
    {
        var random = new ScriptedRandomSource();
        Action negativeHit = () => _ = new HitContext(-1, 0, 0, 0, 0, random);
        Action missingHitRandom = () => _ = new HitContext(0, 0, 0, 0, 0, null!);
        Action negativeDamage = () => _ = new DamageContext(AttackerKind.Character, 0, 0, -1, 0, false, random);
        Action missingDamageRandom = () => _ = new DamageContext(AttackerKind.Character, 0, 0, 0, 0, false, null!);

        Assert.That(negativeHit, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(missingHitRandom, Throws.ArgumentNullException);
        Assert.That(negativeDamage, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(missingDamageRandom, Throws.ArgumentNullException);
    }

    [Test]
    public void FirstSliceScenario_AdventurerAgainstTrainingSlime_IsFullyDetermined()
    {
        DerivedStats adventurer = new RenewalCharacterRules().CalculateDerivedStats(
            new CharacterBuild(1, new PrimaryStats(5, 5, 5, 5, 5, 5), 60, 8, 20, 3, 44, 5f));
        var random = new SeededRandomSource(12345);

        AttackTiming timing = Rules.CalculateAttackTiming(AttackContext.ForAttackSpeed(adventurer.AttackSpeed));
        HitResult hit = Rules.CalculateHit(
            new HitContext(adventurer.Hit, adventurer.Critical, 102, 0, 0, random));
        DamageResult damage = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                adventurer.PhysicalAttack,
                0,
                2,
                0,
                hit.Outcome == HitOutcome.Critical,
                random));

        // Seed 12345 draws 133 and 204 of 1000, then 11 of 100: no dodge, no critical (25 per thousand), and a
        // hit because 11 is under 80. Damage is 2 x 7 = 14, times 4002 / 4020 against defense 2, truncated.
        Assert.That(adventurer.PhysicalAttack, Is.EqualTo(7));
        Assert.That(adventurer.Hit, Is.EqualTo(182));
        Assert.That(adventurer.Critical, Is.EqualTo(25));
        Assert.That(adventurer.AttackSpeed, Is.EqualTo(153));
        Assert.That(timing.Interval, Is.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(hit.HitChancePercent, Is.EqualTo(80));
        Assert.That(hit.Outcome, Is.EqualTo(HitOutcome.Hit));
        Assert.That(damage.Amount, Is.EqualTo(13));
    }
}
}
