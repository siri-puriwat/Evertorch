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
    [TestCase(147, 1060, 530, 265)]
    [TestCase(148, 1040, 520, 260)]
    [TestCase(143, 1140, 570, 285)]
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

    // Equipment research note: a level 1 adventurer (status attack 7) against the slime (hard defense 2). The training
    // sword (attack 20, variance 1) draws 0, 1, or 2 for a weapon part of 19, 20, or 21; the training staff (attack 8,
    // variance 0) still draws once, from 1.
    [TestCase(20, 0, 32)]
    [TestCase(20, 1, 33)]
    [TestCase(20, 2, 34)]
    [TestCase(8, 0, 21)]
    public void CalculateDamage_Armed_DrawsTheWeaponsVarianceOnce(int weaponAttack, int draw, int expected)
    {
        var random = new ScriptedRandomSource(draw);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Character, 7, weaponAttack, 2, 0, false, random));

        Assert.That(result.Amount, Is.EqualTo(expected));
        Assert.That(random.RequestedBounds, Is.EqualTo(new[] { 2 * (weaponAttack * 5 / 100) + 1 }));
    }

    // Cloth armor's hard defense 8 against a crawler (attack 10, rolled 8 to 12) and the adventurer's soft defense 4.
    [TestCase(4, 8, 7)]
    [TestCase(4, 0, 8)]
    [TestCase(0, 8, 3)]
    [TestCase(0, 0, 4)]
    public void CalculateDamage_OnAnArmoredCharacter_AppliesTheArmorAsHardDefense(
        int draw,
        int hardDefense,
        int expected)
    {
        var random = new ScriptedRandomSource(draw);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 10, hardDefense, 4, false, random));

        Assert.That(result.Amount, Is.EqualTo(expected));
    }

    // status attack, weapon attack, hard defense, soft defense => damage, unarmed: no draw at all
    [TestCase(25, 0, 2, 0, 49)]
    [TestCase(1, 0, 2, 0, 1)]
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

    // Refining research note: the training sword at +7 adds a flat 14 after its draw, which the draw never varies.
    [TestCase(0, 46)]
    [TestCase(1, 47)]
    [TestCase(2, 48)]
    public void CalculateDamage_WithARefinedWeapon_AddsItsFlatAttackAfterTheDraw(int draw, int expected)
    {
        var random = new ScriptedRandomSource(draw);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Character, 7, 20, 2, 0, false, random, flatAttack: 14));

        Assert.That(result.Amount, Is.EqualTo(expected), "raw 14 + 19 to 21 + 14, then the slime's defense 2");
        Assert.That(random.RequestedBounds, Is.EqualTo(new[] { 3 }), "the weapon's draw alone");
    }

    // Gameplay Systems §11.1, from Milestone 18 (owner decision 21): damage to a boss grows the weapon and flat parts
    // by the worn cards' percentage, rounded down, before the defense, and draws nothing more.
    [TestCase(0, 52)]
    [TestCase(1, 53)]
    [TestCase(2, 55)]
    public void CalculateDamage_ToABoss_GrowsTheWeaponAndFlatPartsByTheCardsPercent(int draw, int expected)
    {
        var random = new ScriptedRandomSource(draw);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                7,
                20,
                2,
                0,
                false,
                random,
                flatAttack: 14,
                modifiers: new DamageModifiers(20, 0)));

        Assert.That(result.Amount, Is.EqualTo(expected), "14 + (19 to 21 + 14) × 120 %, then the defense of 2");
        Assert.That(random.RequestedBounds, Is.EqualTo(new[] { 3 }), "the weapon's draw alone");
    }

    // A boss's damage shrinks by the defender's reduction, at most 90 %, rounded down, and at least 1.
    [TestCase(100, 25, 75)]
    [TestCase(100, 90, 10)]
    [TestCase(100, 95, 10)]
    [TestCase(5, 90, 1)]
    public void CalculateDamage_FromABoss_ShrinksByTheReduction_CappedAtNinety(int attack, int reduction, int expected)
    {
        var random = new ScriptedRandomSource(attack * 20 / 100);

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Monster,
                0,
                attack,
                0,
                0,
                false,
                random,
                modifiers: new DamageModifiers(0, reduction)));

        Assert.That(result.Amount, Is.EqualTo(expected));
    }

    // A sword meets a target's size, and a staff and bare hands meet none (Gameplay Systems §11.1).
    [TestCase(null, BodySize.Small, 100)]
    [TestCase(null, BodySize.Large, 100)]
    [TestCase(WeaponType.Sword, BodySize.Small, 75)]
    [TestCase(WeaponType.Sword, BodySize.Medium, 100)]
    [TestCase(WeaponType.Sword, BodySize.Large, 75)]
    [TestCase(WeaponType.Staff, BodySize.Small, 100)]
    [TestCase(WeaponType.Staff, BodySize.Medium, 100)]
    [TestCase(WeaponType.Staff, BodySize.Large, 100)]
    public void SizePercent_ForAWeaponAndASize_IsTheSizeTable(WeaponType? weapon, BodySize size, int expected)
    {
        Assert.That(RenewalCombatRules.SizePercent(weapon, size), Is.EqualTo(expected));
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
    public void CalculateDamage_ArmedCritical_TakesTheTopOfTheVarianceWithoutADraw()
    {
        var random = new ScriptedRandomSource();

        DamageResult result =
            Rules.CalculateDamage(new DamageContext(AttackerKind.Character, 7, 20, 2, 0, true, random));

        Assert.That((result.Amount, result.IsCritical), Is.EqualTo((47, true)), "raw 35, 34 after defense, x1.4");
        Assert.That(random.RequestedBounds, Is.Empty);
    }

    [Test]
    public void CalculateDamage_CriticalOnABoss_GrowsThePartsThenMultipliesTheCritical()
    {
        DamageResult result = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                7,
                20,
                2,
                0,
                true,
                new ScriptedRandomSource(),
                flatAttack: 14,
                modifiers: new DamageModifiers(20, 0)));

        Assert.That(result.Amount, Is.EqualTo(77), "(21 + 14) × 120 % = 42; 14 + 42 = 56, 55 after the defense; × 1.4");
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
    public void CalculateDamage_OnTheMantleAtTen_TakesItsDefenseOfNinetyFour()
    {
        DamageResult bare = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 100, 22, 0, true, new ScriptedRandomSource()));
        DamageResult refined = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Monster, 0, 100, 94, 0, true, new ScriptedRandomSource()));

        Assert.That((bare.Amount, refined.Amount), Is.EqualTo((159, 138)),
            "a top roll of 120: x 4,022 / 4,220 at +0, x 4,094 / 4,940 at +10, then x1.4");
    }

    [Test]
    public void CalculateDamage_WithARefinedWeaponCritical_TakesTheTopAndTheFlatAttack()
    {
        var random = new ScriptedRandomSource();

        DamageResult result = Rules.CalculateDamage(
            new DamageContext(AttackerKind.Character, 7, 20, 2, 0, true, random, flatAttack: 14));

        Assert.That((result.Amount, result.IsCritical), Is.EqualTo((67, true)), "raw 49, 48 after defense, x1.4");
        Assert.That(random.RequestedBounds, Is.Empty);
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

    // From Milestone 18 no card adds to magic (owner decisions 13 and 21), and a boss's magic still shrinks last.
    [Test]
    public void CalculateMagicDamage_FromABoss_ShrinksLast_AndNoCardGrowsIt()
    {
        DamageResult toBoss = Rules.CalculateMagicDamage(
            new MagicDamageContext(50, 0, 0, new ScriptedRandomSource(10), modifiers: new DamageModifiers(20, 0)));
        DamageResult fromBoss = Rules.CalculateMagicDamage(
            new MagicDamageContext(50, 0, 0, new ScriptedRandomSource(10), modifiers: new DamageModifiers(0, 25)));
        DamageResult plain = Rules.CalculateMagicDamage(new MagicDamageContext(50, 0, 0, new ScriptedRandomSource(10)));

        Assert.That((plain.Amount, toBoss.Amount, fromBoss.Amount), Is.EqualTo((50, 50, 37)));
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
    public void DamageContext_ForSkill_KeepsEveryInput_ButTheRatioAndTheCritical()
    {
        var random = new ScriptedRandomSource();
        var context = new DamageContext(
            AttackerKind.Character,
            7,
            20,
            2,
            4,
            true,
            random,
            flatAttack: 14,
            modifiers: new DamageModifiers(
                20,
                5,
                Element.Fire,
                Element.Earth,
                WeaponType.Sword,
                BodySize.Small,
                1,
                2,
                3,
                4));

        DamageContext skill = context.ForSkill(130);

        Assert.That(
            (skill.AttackerKind, skill.StatusAttack, skill.WeaponAttack, skill.HardDefense, skill.SoftDefense,
                skill.IsCritical, skill.RatioPercent, skill.FlatAttack, skill.Random),
            Is.EqualTo((AttackerKind.Character, 7, 20, 2, 4, false, 130, 14, (IRandomSource)random)));
        Assert.That(
            (skill.Modifiers.BossDamagePercent, skill.Modifiers.BossDamageReductionPercent,
                skill.Modifiers.AttackElement, skill.Modifiers.DefenseElement, skill.Modifiers.Weapon,
                skill.Modifiers.DefenderSize),
            Is.EqualTo((20, 5, (Element?)Element.Fire, Element.Earth, (WeaponType?)WeaponType.Sword, BodySize.Small)));
        Assert.That(
            (skill.Modifiers.ElementDamagePercent, skill.Modifiers.RaceDamagePercent, skill.Modifiers.SizeDamagePercent,
                skill.Modifiers.ElementDamageReductionPercent),
            Is.EqualTo((1, 2, 3, 4)));
    }

    [Test]
    public void DamageModifiers_ByDefault_ChangeNothing_AndRefuseANegative()
    {
        Action negative = () => _ = new DamageModifiers(-1, 0);
        Action negativeRace = () => _ = new DamageModifiers(0, 0, raceDamagePercent: -1);
        Action negativeReduction = () => _ = new DamageModifiers(0, 0, elementDamageReductionPercent: -1);
        DamageModifiers none = default;

        Assert.That((none.Dealt(0), none.Dealt(4567), none.Taken(1), none.Taken(4567)),
            Is.EqualTo((0L, 4567L, 1L, 4567L)));
        Assert.That(none.AttackElement, Is.Null, "no element step");
        Assert.That(negative, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(negativeRace, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(negativeReduction, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // The defender's cards take at most 90 % off a hit, for an element and from a boss alike, each rounded down and
    // leaving at least 1; the attacker's grow the parts in order, each rounded down (Gameplay Systems §11.1).
    [Test]
    public void DamageModifiers_DealtAndTaken_StepInOrder_AndCapEachReductionAtNinety()
    {
        var dealt = new DamageModifiers(10, 0, elementDamagePercent: 10, raceDamagePercent: 10, sizeDamagePercent: 10);
        var taken = new DamageModifiers(0, 95, elementDamageReductionPercent: 95);

        Assert.That(dealt.Dealt(100), Is.EqualTo(146L), "110, 121, 133, then 146");
        Assert.That(taken.Taken(1000), Is.EqualTo(10L), "100, then 10");
        Assert.That(taken.Taken(5), Is.EqualTo(1L), "at least 1");
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

    [Test]
    public void HitAndMagicContexts_CopyThemselves_KeepingEveryOtherInput()
    {
        var random = new ScriptedRandomSource();

        HitContext hit = new HitContext(182, 25, 102, 3, 4, random).WithoutCritical();
        MagicDamageContext magic = new MagicDamageContext(
                50,
                1,
                2,
                random,
                modifiers: new DamageModifiers(0, 25, Element.Ghost, Element.Water, elementDamageReductionPercent: 30))
            .ForSkill(150);

        Assert.That(
            (hit.AttackerHit, hit.AttackerCritical, hit.DefenderFlee, hit.DefenderPerfectDodge, hit.DefenderLuk),
            Is.EqualTo((182, 0, 102, 3, 4)));
        Assert.That(
            (magic.MagicAttack, magic.HardMagicDefense, magic.SoftMagicDefense, magic.RatioPercent,
                magic.Modifiers.BossDamageReductionPercent, magic.Modifiers.AttackElement,
                magic.Modifiers.DefenseElement,
                magic.Modifiers.ElementDamageReductionPercent),
            Is.EqualTo((50, 1, 2, 150, 25, (Element?)Element.Ghost, Element.Water, 30)));
    }
}
}
