using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
/// <summary>
///     The refining research note's vectors (Gameplay Systems §11.6): one draw from 0 to 99 decides an attempt, below
///     the next level's chance a success; a failure does what its table says; a level gives its table's bonus.
/// </summary>
[TestFixture]
public sealed class RefineRulesTests
{
    private static readonly RefineTableDefinition Training = Table(
        RefineFailure.Destroy,
        0,
        new[] { 100, 100, 100, 100, 100, 100, 100, 60, 40, 20 },
        2);

    private static readonly RefineTableDefinition Dungeon = Table(
        RefineFailure.Destroy,
        0,
        new[] { 100, 100, 100, 100, 100, 100, 60, 40, 30, 20 },
        3);

    private static readonly RefineTableDefinition Armor = new(
        new RefineDefinitionId("refine.armor"),
        new ItemDefinitionId("item.material.wardstone"),
        1,
        RefineFailure.Destroy,
        0,
        new[] { 100, 100, 100, 100, 60, 50, 40, 30, 20, 10 }
            .Zip(new[] { 5, 10, 15, 20, 28, 36, 44, 52, 62, 72 }, (chance, bonus) => new RefineStep(chance, 60, bonus))
            .ToArray());

    private static RefineTableDefinition Table(RefineFailure failure, int downgradeBy, int[] chances, int bonusPerLevel)
    {
        return new RefineTableDefinition(
            new RefineDefinitionId("refine.a"),
            new ItemDefinitionId("item.material.emberstone"),
            1,
            failure,
            downgradeBy,
            chances.Select((chance, index) => new RefineStep(chance, 20, bonusPerLevel * (index + 1))).ToArray());
    }

    [TestCase(0, 99, RefineOutcomeKind.Refined, 1)]
    [TestCase(6, 99, RefineOutcomeKind.Refined, 7)]
    [TestCase(7, 59, RefineOutcomeKind.Refined, 8)]
    [TestCase(7, 60, RefineOutcomeKind.Destroyed, 0)]
    [TestCase(9, 19, RefineOutcomeKind.Refined, 10)]
    [TestCase(9, 20, RefineOutcomeKind.Destroyed, 0)]
    public void Resolve_OnTheTrainingTable_IsSafeToSeven_ThenDestroysOnAFailure(
        int level,
        int draw,
        RefineOutcomeKind outcome,
        int after)
    {
        RefineResolution resolution = RefineRules.Resolve(Training, level, draw);

        Assert.That((resolution.Outcome, resolution.Level), Is.EqualTo((outcome, after)));
    }

    [TestCase(1, 8, 7)]
    [TestCase(9, 5, 0)]
    [TestCase(2, 1, 0)]
    public void Resolve_OnATableThatLowers_FallsByItsLevels_ToZeroAtTheLowest(int downgradeBy, int level, int after)
    {
        RefineTableDefinition lowers = Table(RefineFailure.Downgrade, downgradeBy, Enumerable.Repeat(50, 10).ToArray(),
            2);

        RefineResolution resolution = RefineRules.Resolve(lowers, level, 50);

        Assert.That((resolution.Outcome, resolution.Level), Is.EqualTo((RefineOutcomeKind.Lowered, after)));
    }

    [TestCase(10, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 100)]
    [TestCase(0, -1)]
    public void Resolve_AtTheTopOrWithADrawOutOfRange_Throws(int level, int draw)
    {
        Action resolve = () => RefineRules.Resolve(Training, level, draw);

        Assert.That(resolve, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(0, 0)]
    [TestCase(7, 14)]
    [TestCase(10, 20)]
    [TestCase(12, 20)]
    public void BonusAt_OnTheTrainingTable_IsTwoALevel_AndTheLastPastItsEnd(int level, int bonus)
    {
        Assert.That(RefineRules.BonusAt(Training, level), Is.EqualTo(bonus));
    }

    [TestCase("training", 0.048)]
    [TestCase("dungeon", 0.0144)]
    [TestCase("armor", 0.00072)]
    public void TheChanceOfTen_FromTheSafeLevel_IsTheProductOfTheChancesPastIt(string name, double expected)
    {
        RefineTableDefinition table = name == "training" ? Training : name == "dungeon" ? Dungeon : Armor;
        int safe = Enumerable.Range(0, 10).Last(level => RefineRules.ChanceFrom(table, level) == 100) + 1;

        double chance = Enumerable.Range(safe, 10 - safe)
            .Aggregate(1d, (product, level) => product * RefineRules.ChanceFrom(table, level) / 100d);

        Assert.That(chance, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void BonusAt_ForArmorAndTheDungeonsWeapons_IsCumulative_AndWithoutATableNothing()
    {
        Assert.That(
            Enumerable.Range(0, 11).Select(level => RefineRules.BonusAt(Armor, level)),
            Is.EqualTo(new[] { 0, 5, 10, 15, 20, 28, 36, 44, 52, 62, 72 }));
        Assert.That(RefineRules.BonusAt(Dungeon, 6), Is.EqualTo(18));
        Assert.That(RefineRules.BonusAt(null, 7), Is.Zero);
    }

    [Test]
    public void ChanceFrom_EachLevel_IsTheNextLevelsChance_AndNothingAtTheTop()
    {
        Assert.That(
            Enumerable.Range(0, 11).Select(level => RefineRules.ChanceFrom(Training, level)),
            Is.EqualTo(new[] { 100, 100, 100, 100, 100, 100, 100, 60, 40, 20, 0 }));
    }

    [Test]
    public void Resolve_FromZeroOnATableThatLowers_LeavesTheItemUnchanged()
    {
        RefineTableDefinition lowers = Table(RefineFailure.Downgrade, 1, Enumerable.Repeat(50, 10).ToArray(), 2);

        RefineResolution resolution = RefineRules.Resolve(lowers, 0, 99);

        Assert.That((resolution.Outcome, resolution.Level), Is.EqualTo((RefineOutcomeKind.Unchanged, 0)));
    }

    [Test]
    public void Resolve_OnATableThatKeeps_LeavesTheItemAsItWas()
    {
        RefineTableDefinition keeps = Table(RefineFailure.Keep, 0, Enumerable.Repeat(50, 10).ToArray(), 2);

        RefineResolution resolution = RefineRules.Resolve(keeps, 8, 50);

        Assert.That((resolution.Outcome, resolution.Level), Is.EqualTo((RefineOutcomeKind.Unchanged, 8)));
    }
}
}
