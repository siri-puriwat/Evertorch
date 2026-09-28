using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's words for a build (Prototype Content §2): the Stats window's rows and the feedback lines a new sheet
///     earns.
/// </summary>
[TestFixture]
public sealed class BuildMessagesTests
{
    private static CharacterSheet Sheet(byte jobLevel, byte agility, byte agilityCost = 2)
    {
        CharacterSheetStat[] stats = Enumerable.Repeat(new CharacterSheetStat(5, 2), CharacterSheet.StatCount)
            .ToArray();
        stats[1] = new CharacterSheetStat(agility, agilityCost);
        return new CharacterSheet(jobLevel, 0, 30, 3, 0, stats, 46, 12, 5, 6, 188, 125, 13, 154);
    }

    [Test]
    public void Describe_SaysWhatANewSheetRaised_AndNothingForTheFirst()
    {
        Assert.That(BuildMessages.Describe(null, Sheet(2, 6)), Is.Empty, "the baseline");
        Assert.That(BuildMessages.Describe(Sheet(1, 5), Sheet(1, 5)), Is.Empty, "nothing changed");
        Assert.That(BuildMessages.Describe(Sheet(1, 5), Sheet(1, 6)), Is.EqualTo(new[] { "AGI 6." }));
        Assert.That(BuildMessages.Describe(Sheet(1, 5), Sheet(3, 5)), Is.EqualTo(new[] { "Job level 3." }));
        Assert.That(
            BuildMessages.Describe(Sheet(1, 5), Sheet(2, 7)),
            Is.EqualTo(new[] { "Job level 2.", "AGI 7." }),
            "the job level first, then each statistic that rose");
        Assert.That(BuildMessages.Describe(Sheet(2, 9), Sheet(2, 5)), Is.Empty, "a lowered statistic says nothing");
    }

    [Test]
    public void TheStatsWindowsWords_NameEachStatistic_ItsNextCost_AndTheDerivedOnes()
    {
        Assert.That(
            Enumerable.Range(1, 6).Select(stat => BuildMessages.StatName((PrimaryStat)stat)),
            Is.EqualTo(new[] { "STR", "AGI", "VIT", "INT", "DEX", "LUK" }));
        Assert.That(BuildMessages.StatValue(PrimaryStat.Agi, new CharacterSheetStat(11, 3)), Is.EqualTo("AGI 11"));
        Assert.That(BuildMessages.NextCost(new CharacterSheetStat(11, 3)), Is.EqualTo("next 3"));
        Assert.That(BuildMessages.NextCost(new CharacterSheetStat(99, 0)), Is.EqualTo("max"));
        Assert.That(BuildMessages.Points(7), Is.EqualTo("Points: 7"));
        Assert.That(
            BuildMessages.Derived(Sheet(1, 5)),
            Is.EqualTo(
                new[]
                {
                    "Attack 46", "Magic attack 12", "Defense 5", "Magic defense 6", "Hit 188", "Flee 125",
                    "Critical 1.3%", "Attack speed 154"
                }));
    }
}
}
