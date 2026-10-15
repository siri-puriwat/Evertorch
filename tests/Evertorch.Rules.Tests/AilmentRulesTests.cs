using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
/// <summary>
///     The status ailments research note's vectors (Gameplay Systems §9.1), Evertorch's own: the level advantage, the
///     resistances, the chance in basis points, the duration, poison's loss and tick, a held, sleeping, or blinded body
///     in the hit, and a frozen or poisoned one in the damage.
/// </summary>
[TestFixture]
public sealed class AilmentRulesTests
{
    private const int TrainingSword = 20;
    private static readonly AilmentRules Ailments = new();
    private static readonly RenewalCombatRules Combat = new();

    // A level-1 Adventurer's training-sword swing at a Forest Crawler (Earth, Small, defense 4), its draw 0.
    private static int Swing(Element? attack, bool isFrozen, bool isPoisoned = false)
    {
        return Combat.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                7,
                TrainingSword,
                4,
                0,
                false,
                new ScriptedRandomSource(0),
                modifiers: new DamageModifiers(
                    0,
                    0,
                    attack,
                    isFrozen ? Element.Water : Element.Earth,
                    WeaponType.Sword,
                    BodySize.Small),
                isDefenderFrozen: isFrozen,
                isDefenderPoisoned: isPoisoned)).Amount;
    }

    private static HitResult Hit(
        ScriptedRandomSource random,
        int hit = 50,
        int critical = 0,
        int flee = 50,
        int perfectDodge = 0,
        int luk = 0,
        bool isHeld = false,
        bool isAsleep = false,
        bool isAttackerBlinded = false,
        bool isDefenderBlinded = false)
    {
        return Combat.CalculateHit(
            new HitContext(hit, critical, flee, perfectDodge, luk, random, isHeld, isAsleep, isAttackerBlinded,
                isDefenderBlinded));
    }

    [TestCase(2, 0)]
    [TestCase(3, 1)]
    [TestCase(11, 24)]
    [TestCase(15, 45)]
    [TestCase(16, 51)]
    [TestCase(24, 115)]
    [TestCase(-5, 0)]
    public void LevelAdvantage_OfAGap_IsItsSquareOverFive(int gap, int expected)
    {
        Assert.That(Ailments.LevelAdvantage(30 + gap, 30), Is.EqualTo(expected));
    }

    [Test]
    public void CalculateDamage_APoisonedDefender_LosesAQuarterOfItsOwnDefense_AfterTheFreeze()
    {
        // A monster's hard defense of 40: frozen 20, poisoned 30, both 15.
        int Monster(bool isFrozen, bool isPoisoned)
        {
            return Combat.CalculateDamage(
                new DamageContext(AttackerKind.Character, 50, 0, 40, 0, false, new ScriptedRandomSource(),
                    isDefenderFrozen: isFrozen, isDefenderPoisoned: isPoisoned)).Amount;
        }

        // A player's soft defense of 20 behind armor of 40: poison cuts the 20 to 15 and leaves the armor.
        int Player(bool isPoisoned)
        {
            return Combat.CalculateDamage(
                new DamageContext(AttackerKind.Monster, 0, 100, 40, 20, false, new ScriptedRandomSource(20),
                    isDefenderPoisoned: isPoisoned)).Amount;
        }

        Assert.That(
            (Monster(false, false), Monster(true, false), Monster(false, true), Monster(true, true)),
            Is.EqualTo((91, 95, 93, 96)));
        Assert.That((Player(false), Player(true)), Is.EqualTo((71, 76)));
        DamageContext copied = new DamageContext(AttackerKind.Character, 1, 0, 0, 0, false, new ScriptedRandomSource(),
            isDefenderFrozen: true, isDefenderPoisoned: true).ForSkill(150);
        Assert.That((copied.IsDefenderFrozen, copied.IsDefenderPoisoned), Is.EqualTo((true, true)));
    }

    [Test]
    public void CalculateDamage_ATrainingSwordOnAFrozenForestCrawler_StrikesWaterAtHalfItsDefense()
    {
        Assert.That(Swing(Element.Wind, false), Is.EqualTo(23), "the Wind Converter on Earth");
        Assert.That(
            (Swing(Element.Neutral, true), Swing(Element.Wind, true), Swing(Element.Fire, true)),
            Is.EqualTo((27, 34, 23)),
            "frozen: Water, defense 2");
    }

    [Test]
    public void CalculateHit_ABlindedAttackerOrDefender_LosesAQuarterOfItsHitOrFlee()
    {
        Assert.That(
            Hit(new ScriptedRandomSource(999, 999, 99), 182, flee: 100, isAttackerBlinded: true).HitChancePercent,
            Is.EqualTo(37), "182 less 45 is 137, against 100");
        Assert.That(
            Hit(new ScriptedRandomSource(999, 999, 99), 182, flee: 100, isDefenderBlinded: true).HitChancePercent,
            Is.EqualTo(100), "the flee is 75");
    }

    [Test]
    public void CalculateHit_AHeldDefender_IsHitButForAPerfectDodge_AndEveryStageStillDraws()
    {
        var missed = new ScriptedRandomSource(999, 999, 99);
        var dodged = new ScriptedRandomSource(0);
        var critical = new ScriptedRandomSource(999, 0);

        HitResult held = Hit(missed, 10, flee: 200, isHeld: true);
        HitResult dodge = Hit(dodged, perfectDodge: 10, isHeld: true);
        HitResult crit = Hit(critical, critical: 10, isHeld: true);

        Assert.That((held.Outcome, held.HitChancePercent), Is.EqualTo((HitOutcome.Hit, 5)));
        Assert.That(missed.RequestedBounds, Is.EqualTo(new[] { 1000, 1000, 100 }));
        Assert.That(dodge.Outcome, Is.EqualTo(HitOutcome.PerfectDodge));
        Assert.That(crit.Outcome, Is.EqualTo(HitOutcome.Critical));
        Assert.That(Hit(new ScriptedRandomSource(999, 999, 99), 10, flee: 200).Outcome, Is.EqualTo(HitOutcome.Miss));
    }

    [Test]
    public void CalculateHit_ASleepingDefender_DoublesTheCriticalChanceAfterLuk()
    {
        // 60 tenths less twice a LUK of 10 is 40, doubled to 80: a draw of 79 is critical, 80 is not.
        Assert.That(Hit(new ScriptedRandomSource(999, 79, 0), critical: 60, luk: 10, isAsleep: true).Outcome,
            Is.EqualTo(HitOutcome.Critical));
        Assert.That(Hit(new ScriptedRandomSource(999, 80, 0), critical: 60, luk: 10, isAsleep: true).Outcome,
            Is.EqualTo(HitOutcome.Hit));
        Assert.That(Hit(new ScriptedRandomSource(999, 40, 0), critical: 60, luk: 10).Outcome,
            Is.EqualTo(HitOutcome.Hit), "awake, 40 is the chance");
        Assert.That(
            new HitContext(50, 60, 50, 0, 10, new ScriptedRandomSource(), true, true, true, true).WithoutCritical(),
            Has.Property(nameof(HitContext.AttackerCritical)).EqualTo(0)
                .And.Property(nameof(HitContext.IsDefenderAsleep)).True
                .And.Property(nameof(HitContext.IsDefenderHeld)).True
                .And.Property(nameof(HitContext.IsAttackerBlinded)).True
                .And.Property(nameof(HitContext.IsDefenderBlinded)).True);
    }

    [Test]
    public void CrushingBite_OnEarthenArmor_IsCutByTheStatisticAndTheCard()
    {
        int resistance = Ailments.PlayerResistance(30, Ailments.LevelAdvantage(14, 20));
        int card = Ailments.CardResistance(30);

        Assert.That(
            (Ailments.LandingChance(1500, 1, resistance, card), Ailments.Duration(4000, resistance, card)),
            Is.EqualTo((735, 1960)));
    }

    [Test]
    public void FrostBreath_OnTheWalksVanguardAndArcanist_IsResistedBySoftMagicDefense()
    {
        int vanguard = Ailments.PlayerResistance(16, 0);
        int arcanist = Ailments.PlayerResistance(41, 0);
        int royal = Ailments.CardResistance(50);

        Assert.That(
            (Ailments.LandingChance(10_000, 1, vanguard, 0), Ailments.Duration(8000, vanguard, 0)),
            Is.EqualTo((8400, 6720)));
        Assert.That(
            (Ailments.LandingChance(10_000, 1, vanguard, royal), Ailments.Duration(8000, vanguard, royal)),
            Is.EqualTo((4200, 3360)));
        Assert.That(
            (Ailments.LandingChance(10_000, 1, arcanist, 0), Ailments.Duration(8000, arcanist, 0)),
            Is.EqualTo((5900, 4720)));
    }

    [Test]
    public void HeavyBlow5_OnAGrottoCrawler_IsUnresisted()
    {
        int resistance = Ailments.MonsterResistance(14, Ailments.LevelAdvantage(25, 14));

        Assert.That(
            (resistance, Ailments.LandingChance(2500, 1, resistance, 0), Ailments.Duration(4000, resistance, 0)),
            Is.EqualTo((0, 2500, 4000)));
    }

    [Test]
    public void Hush_OnALevel20Arcanist_LandsByIntelligence()
    {
        int resistance = Ailments.PlayerResistance(40, Ailments.LevelAdvantage(16, 20));

        Assert.That(
            (Ailments.LandingChance(10_000, 1, resistance, 0), Ailments.Duration(15_000, resistance, 0)),
            Is.EqualTo((6000, 9000)));
    }

    [Test]
    public void LandingChance_ScaledAndResisted_IsCappedAtCertain_AndADurationLastsASecondAtLeast()
    {
        Assert.That(Ailments.LandingChance(2000, 5, 0, 0), Is.EqualTo(10_000), "2,000 × 5, capped");
        Assert.That(Ailments.LandingChance(2000, 3, 0, 0), Is.EqualTo(6000));
        Assert.That(Ailments.LandingChance(10_000, 100, 100, 0), Is.Zero, "fully resisted");
        Assert.That(Ailments.LandingChance(1, 1, 50, 0), Is.Zero, "rounded down");
        Assert.That(Ailments.Duration(4000, 90, 90), Is.EqualTo(1000), "40 ms, raised to the floor");
        Assert.That(Ailments.Duration(86_400_000, 0, 0), Is.EqualTo(86_400_000));
    }

    [Test]
    public void Lull_OnALevel1_FromLevel17_IsCertain()
    {
        int resistance = Ailments.PlayerResistance(40, Ailments.LevelAdvantage(17, 1));

        Assert.That(
            (Ailments.LevelAdvantage(17, 1), resistance, Ailments.LandingChance(10_000, 1, resistance, 0)),
            Is.EqualTo((51, 0, 10_000)));
    }

    [Test]
    public void PoisonLoss_OfAMonster_IsAThirdOfAPlayers_AndATickNeverTakesTheLast()
    {
        Assert.That(Ailments.PoisonLoss(1000, false), Is.EqualTo(7));
        Assert.That(Ailments.PoisonLoss(1000, true), Is.EqualTo(17));
        Assert.That(Ailments.DoesPoisonTick(5, 8, 5), Is.False, "a loss as large as the HP never fires");
        Assert.That(Ailments.DoesPoisonTick(6, 8, 5), Is.True);
    }

    [Test]
    public void Poison_OnANewAdventurer_Ticks18Times_From71_EndingAt17()
    {
        const int maxHp = 71;
        int loss = Ailments.PoisonLoss(maxHp, true);
        int hp = maxHp;
        int ticks = 0;
        while (Ailments.DoesPoisonTick(hp, maxHp, loss))
        {
            hp -= loss;
            ticks++;
        }

        Assert.That((loss, ticks, hp), Is.EqualTo((3, 18, 17)));
    }

    [Test]
    public void Resistances_AreClampedFrom0To100()
    {
        Assert.That(
            (Ailments.PlayerResistance(140, 0), Ailments.PlayerResistance(5, 51), Ailments.MonsterResistance(25, 1),
                Ailments.CardResistance(130), Ailments.CardResistance(-1)),
            Is.EqualTo((100, 0, 24, 100, 0)));
    }

    [Test]
    public void SheetResistance_JoinsTheStatisticAndTheCard()
    {
        Assert.That(
            (Ailments.SheetResistance(30, 30), Ailments.SheetResistance(16, 50), Ailments.SheetResistance(40, 0),
                Ailments.SheetResistance(100, 0), Ailments.SheetResistance(0, 0)),
            Is.EqualTo((51, 58, 40, 100, 0)));
    }

    [Test]
    public void VenomBite_OnANewAdventurer_Lands1900In10000_For19Seconds()
    {
        int resistance = Ailments.PlayerResistance(5, Ailments.LevelAdvantage(3, 1));

        Assert.That(
            (Ailments.LandingChance(2000, 1, resistance, 0), Ailments.Duration(20_000, resistance, 0)),
            Is.EqualTo((1900, 19_000)));
    }
}
}
