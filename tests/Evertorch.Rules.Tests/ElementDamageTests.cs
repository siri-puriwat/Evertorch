using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
/// <summary>
///     The elements, races, and sizes research note's vectors (Gameplay Systems §3, §9, §11.1): a level-1 Adventurer
///     with every statistic 5, a status attack of 7, a soft defense of 4, and a soft magic defense of 7, against the
///     content's monsters, Milestone 16's dungeon table at +3 a level, and the cards as Milestone 18 retunes them.
/// </summary>
[TestFixture]
public sealed class ElementDamageTests
{
    private const int StatusAttack = 7;
    private const int TrainingSword = 20;
    private const int IronSword = 38;
    private const int TrainingStaff = 8;
    private static readonly RenewalCombatRules Rules = new();

    // A player's swing, its weapon's draw scripted, at a monster of the given defense, element, and size.
    private static DamageResult Swing(
        int weaponAttack,
        int draw,
        int defense,
        Element attack,
        Element defender,
        BodySize size,
        WeaponType? weapon = WeaponType.Sword,
        int flat = 0,
        bool isCritical = false,
        int raceDamage = 0,
        int sizeDamage = 0,
        int bossDamage = 0,
        int statusAttack = StatusAttack)
    {
        ScriptedRandomSource random = isCritical ? new ScriptedRandomSource() : new ScriptedRandomSource(draw);
        return Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                statusAttack,
                weaponAttack,
                defense,
                0,
                isCritical,
                random,
                flatAttack: flat,
                modifiers: new DamageModifiers(
                    bossDamage,
                    0,
                    attack,
                    defender,
                    weaponAttack == 0 ? null : weapon,
                    size,
                    raceDamagePercent: raceDamage,
                    sizeDamagePercent: sizeDamage)));
    }

    // A cast's magic attack, its draw scripted, at a target of the given soft magic defense and element.
    private static DamageResult Cast(
        int magicAttack,
        int draw,
        int softMagicDefense,
        Element attack,
        Element defender,
        int ratio = 100,
        int elementReduction = 0,
        int bossReduction = 0)
    {
        return Rules.CalculateMagicDamage(
            new MagicDamageContext(
                magicAttack,
                0,
                softMagicDefense,
                new ScriptedRandomSource(draw),
                ratio,
                new DamageModifiers(
                    0,
                    bossReduction,
                    attack,
                    defender,
                    elementDamageReductionPercent: elementReduction)));
    }

    // The Barrow Crawler's Grave Bite, attack 58, its draw 12 of Next(24), at 150 %, on a player.
    private static DamageResult GraveBite(int hardDefense, Element armor, int elementReduction = 0)
    {
        return Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Monster,
                0,
                58,
                hardDefense,
                4,
                false,
                new ScriptedRandomSource(12),
                150,
                modifiers: new DamageModifiers(
                    0,
                    0,
                    Element.Shadow,
                    armor,
                    elementDamageReductionPercent: elementReduction)));
    }

    [Test]
    public void CalculateDamage_AMonsterSkillOnAZeroCell_IsImmune_AndASwingTakesNoElement()
    {
        DamageResult immune = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Monster,
                0,
                58,
                15,
                4,
                false,
                new ScriptedRandomSource(12),
                150,
                modifiers: new DamageModifiers(0, 0, Element.Shadow, Element.Undead)));
        DamageResult swing = Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Monster,
                0,
                58,
                15,
                4,
                false,
                new ScriptedRandomSource(12),
                modifiers: new DamageModifiers(0, 0, null, Element.Undead)));

        Assert.That((immune.Amount, immune.IsImmune), Is.EqualTo((0, true)), "Shadow on Undead");
        Assert.That((swing.Amount, swing.IsImmune), Is.EqualTo((52, false)), "⌊58 × 4015 ÷ 4150⌋ − 4, no element");
    }

    [Test]
    public void CalculateDamage_AStaff_IgnoresSize()
    {
        Assert.That(
            Swing(TrainingStaff, 0, 4, Element.Neutral, Element.Earth, BodySize.Small, WeaponType.Staff).Amount,
            Is.EqualTo(21),
            "14 + 8, less the defense of 4");
    }

    [Test]
    public void CalculateDamage_ASwordOnASmallCrawler_IsSizedAndTakesTheConvertersElement()
    {
        Assert.That(
            Swing(TrainingSword, 0, 4, Element.Neutral, Element.Earth, BodySize.Small).Amount,
            Is.EqualTo(27),
            "14 + ⌊19 × 75 %⌋ = 28, less the defense of 4");
        Assert.That(
            Swing(TrainingSword, 0, 4, Element.Fire, Element.Earth, BodySize.Small).Amount,
            Is.EqualTo(34),
            "the weapon part ⌊14 × 150 %⌋ = 21");
        Assert.That(
            Swing(TrainingSword, 0, 4, Element.Neutral, Element.Earth, BodySize.Small, isCritical: true).Amount,
            Is.EqualTo(39),
            "the top, 21, sized to 15; 28 after the defense; × 1.4");
        Assert.That(
            Swing(TrainingSword, 0, 4, Element.Neutral, Element.Earth, BodySize.Small, flat: 8, sizeDamage: 10).Amount,
            Is.EqualTo(37),
            "Keen: 14 + 8 = 22, small + 10 % = 24");
    }

    [Test]
    public void CalculateDamage_ASwordOnTheGhost_HalvesItsNeutralParts()
    {
        Assert.That(
            Swing(TrainingSword, 0, 8, Element.Neutral, Element.Ghost, BodySize.Small).Amount,
            Is.EqualTo(12),
            "2 × ⌊7 × 50 %⌋ = 6 and ⌊14 × 50 %⌋ = 7");
        Assert.That(
            Swing(TrainingSword, 0, 8, Element.Earth, Element.Ghost, BodySize.Small).Amount,
            Is.EqualTo(19),
            "an Earth Converter: 6 + 14");
        Assert.That(
            Swing(0, 0, 8, Element.Neutral, Element.Ghost, BodySize.Small).Amount,
            Is.EqualTo(5),
            "bare hands: 6");
    }

    [Test]
    public void CalculateDamage_GraveBite_TakesShadowAfterTheDefenseAndTheReductionLast()
    {
        Assert.That(GraveBite(15, Element.Neutral).Amount, Is.EqualTo(80), "⌊87 × 4015 ÷ 4150⌋ − 4 on Leather Armor");
        Assert.That(GraveBite(15, Element.Neutral, 30).Amount, Is.EqualTo(56), "Shrouded's 30 % less Shadow");
        Assert.That(GraveBite(25, Element.Earth).Amount, Is.EqualTo(78),
            "Earthen: defense 25, and Shadow on Earth 100");
    }

    [Test]
    public void CalculateDamage_TheIronSwordOnTheLargeBoss_IsSizedAndTheCardsGrowItsParts()
    {
        Assert.That(
            Swing(IronSword, 1, 40, Element.Neutral, Element.Water, BodySize.Large).Amount,
            Is.EqualTo(38),
            "14 + ⌊38 × 75 %⌋ = 42, less the defense of 40");
        Assert.That(
            Swing(IronSword, 1, 40, Element.Wind, Element.Water, BodySize.Large).Amount,
            Is.EqualTo(51),
            "a Wind Converter: 28 × 150 % = 42");
        Assert.That(
            Swing(IronSword, 1, 40, Element.Wind, Element.Water, BodySize.Large, flat: 21).Amount,
            Is.EqualTo(79),
            "at +7: the flat 21 × 150 % = 31");
        Assert.That(
            Swing(IronSword, 1, 40, Element.Wind, Element.Water, BodySize.Large, flat: 21, bossDamage: 20).Amount,
            Is.EqualTo(92),
            "Bane: 42 + 31 = 73, bosses + 20 % = 87");
    }

    [Test]
    public void CalculateDamage_TheIronSwordOnTheUndead_TakesFireAndARacesCard()
    {
        Assert.That(
            Swing(IronSword, 1, 20, Element.Neutral, Element.Undead, BodySize.Medium).Amount,
            Is.EqualTo(49),
            "14 + 38, less the defense of 20");
        Assert.That(
            Swing(IronSword, 1, 20, Element.Fire, Element.Undead, BodySize.Medium).Amount,
            Is.EqualTo(67),
            "Fire on the Undead: 14 + 57");
        Assert.That(
            Swing(IronSword, 1, 20, Element.Fire, Element.Undead, BodySize.Medium, raceDamage: 20).Amount,
            Is.EqualTo(78),
            "Spectral, the Undead race + 20 %: 14 + 68");
    }

    [Test]
    public void CalculateDamage_WhenEveryPartReachesZero_IsImmune()
    {
        DamageResult result = Swing(0, 0, 8, Element.Neutral, Element.Ghost, BodySize.Small, statusAttack: 1);

        Assert.That((result.Amount, result.IsImmune), Is.EqualTo((0, true)), "⌊1 × 50 %⌋ = 0, unarmed");
    }

    [Test]
    public void CalculateDamage_WithElementsAndCards_DrawsNothingMore()
    {
        var plain = new ScriptedRandomSource(1);
        var modified = new ScriptedRandomSource(1);

        Rules.CalculateDamage(new DamageContext(AttackerKind.Character, 7, TrainingSword, 4, 0, false, plain));
        Rules.CalculateDamage(
            new DamageContext(
                AttackerKind.Character,
                7,
                TrainingSword,
                4,
                0,
                false,
                modified,
                flatAttack: 8,
                modifiers: new DamageModifiers(
                    20,
                    0,
                    Element.Fire,
                    Element.Earth,
                    WeaponType.Sword,
                    BodySize.Small,
                    10,
                    10,
                    10)));

        Assert.That(modified.RequestedBounds, Is.EqualTo(plain.RequestedBounds).And.EqualTo(new[] { 3 }));
    }

    [Test]
    public void CalculateMagicDamage_ArcaneBoltAsGhost_GrowsOnAGhostAndHalvesOnNeutral()
    {
        Assert.That(Cast(40, 8, 0, Element.Ghost, Element.Ghost).Amount, Is.EqualTo(60), "on the Hollow Wisp");
        Assert.That(Cast(40, 8, 0, Element.Ghost, Element.Water).Amount, Is.EqualTo(40), "on the slime");
        Assert.That(Cast(40, 8, 0, Element.Ghost, Element.Neutral).Amount, Is.EqualTo(20), "on a Neutral monster");
    }

    [Test]
    public void CalculateMagicDamage_OnAZeroCell_IsImmune()
    {
        DamageResult result = Cast(40, 8, 0, Element.Holy, Element.Holy);

        Assert.That((result.Amount, result.IsImmune), Is.EqualTo((0, true)),
            "a test package's Holy bolt on a Holy slime");
    }

    [Test]
    public void CalculateMagicDamage_SparkBoltOnArmor_TakesItsElementAndTheReduction()
    {
        Assert.That(Cast(20, 4, 7, Element.Wind, Element.Neutral).Amount, Is.EqualTo(13), "16 + 4 − 7");
        Assert.That(Cast(20, 4, 7, Element.Wind, Element.Earth).Amount, Is.EqualTo(9), "Earthen: Wind on Earth 75");
        Assert.That(
            Cast(20, 4, 7, Element.Wind, Element.Neutral, elementReduction: 20).Amount,
            Is.EqualTo(10),
            "Hardy: 20 % less Wind");
    }

    [Test]
    public void CalculateMagicDamage_TheSlamOnArmor_IsEarth_AndAMuffledOneStrikesForOne()
    {
        Assert.That(Cast(150, 30, 7, Element.Earth, Element.Neutral, 120).Amount, Is.EqualTo(173), "180 − 7");
        Assert.That(Cast(150, 30, 7, Element.Earth, Element.Earth, 120).Amount, Is.EqualTo(43), "Earthen: 25 %");
        Assert.That(
            Cast(150, 30, 7, Element.Earth, Element.Neutral, 120, bossReduction: 25).Amount,
            Is.EqualTo(129),
            "Royal instead: 25 % less from a boss");
        Assert.That(Cast(0, 0, 7, Element.Earth, Element.Earth, 120).Amount, Is.EqualTo(1), "a muffled slam");
    }
}
}
