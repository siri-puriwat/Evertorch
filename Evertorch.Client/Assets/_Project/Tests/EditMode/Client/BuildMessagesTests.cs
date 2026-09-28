using System.Linq;
using Evertorch.Game;
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

    private static SkillListEntry Entry(string skill, byte level, byte max, byte prerequisite = 255, byte needs = 0)
    {
        return new SkillListEntry(new SkillDefinitionId(skill), 0f, 0, 0, 0, 0, level, max, prerequisite, needs);
    }

    [Test]
    public void DescribeSkills_SaysEachSkillWhoseLevelRose_AndNothingForTheFirstList()
    {
        SkillListEntry[] before = { Entry("skill.strike", 1, 5), Entry("skill.focus", 0, 3, 0, 1) };
        SkillListEntry[] after = { Entry("skill.strike", 2, 5), Entry("skill.focus", 1, 3, 0, 1) };

        Assert.That(BuildMessages.DescribeSkills(null, after, null), Is.Empty, "the baseline");
        Assert.That(BuildMessages.DescribeSkills(before, before, null), Is.Empty, "a cooldown's new list");
        Assert.That(
            BuildMessages.DescribeSkills(before, after, null),
            Is.EqualTo(new[] { "skill.strike Lv 2.", "skill.focus Lv 1." }),
            "without content, the definition names it");
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
    public void TheSkillsWindowsWords_GiveTheLevelOfTheMaximum_AndAnUnmetPrerequisite()
    {
        SkillListEntry[] tree = { Entry("skill.strike", 0, 5), Entry("skill.focus", 0, 3, 0, 1) };
        SkillListEntry[] learned = { Entry("skill.strike", 1, 5), tree[1] };

        Assert.That(BuildMessages.SkillLevel(Entry("skill.strike", 2, 5)), Is.EqualTo("Lv 2/5"));
        Assert.That(BuildMessages.UnmetPrerequisite(tree, tree[0], null), Is.Null, "no prerequisite");
        Assert.That(BuildMessages.UnmetPrerequisite(tree, tree[1], null), Is.EqualTo("Needs skill.strike Lv 1"));
        Assert.That(BuildMessages.UnmetPrerequisite(learned, learned[1], null), Is.Null, "met");
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
