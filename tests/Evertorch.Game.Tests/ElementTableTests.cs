using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class ElementTableTests
{
    // The research note's table, attacker by row and defender by column, both in the order of Element: Neutral,
    // Water, Earth, Fire, Wind, Poison, Holy, Shadow, Ghost, Undead.
    private static readonly int[,] Table =
    {
        { 100, 100, 100, 100, 100, 100, 100, 100, 50, 100 },
        { 100, 25, 100, 150, 75, 100, 100, 100, 100, 100 },
        { 100, 100, 25, 75, 150, 100, 100, 100, 100, 100 },
        { 100, 75, 150, 25, 100, 100, 100, 100, 100, 150 },
        { 100, 150, 75, 100, 25, 100, 100, 100, 100, 100 },
        { 100, 125, 125, 125, 125, 0, 50, 50, 50, 50 },
        { 100, 100, 100, 100, 100, 100, 0, 150, 100, 150 },
        { 100, 100, 100, 100, 100, 100, 150, 0, 100, 0 },
        { 50, 100, 100, 100, 100, 100, 100, 100, 150, 100 },
        { 100, 100, 100, 100, 100, 100, 100, 0, 100, 0 }
    };

    private static Element[] Elements()
    {
        return (Element[])Enum.GetValues(typeof(Element));
    }

    [Test]
    public void Element_Values_AreTheWireNumbersZeroToNine()
    {
        Assert.That(
            Elements().Select(element => (byte)element),
            Is.EqualTo(Enumerable.Range(0, 10).Select(value => (byte)value)));
        Assert.That(
            Elements(),
            Is.EqualTo(
                new[]
                {
                    Element.Neutral, Element.Water, Element.Earth, Element.Fire, Element.Wind, Element.Poison,
                    Element.Holy, Element.Shadow, Element.Ghost, Element.Undead
                }));
    }

    [Test]
    public void Of_ASkillOfItsOwnElement_StrikesWithIt()
    {
        foreach (Element element in Elements())
        {
            var skill = (SkillElement)Enum.Parse(typeof(SkillElement), element.ToString());

            Assert.That(ElementTable.Of(skill, Element.Fire), Is.EqualTo(element), skill.ToString());
        }
    }

    [Test]
    public void Of_ASkillOfTheWeapons_StrikesWithTheAttackElement()
    {
        foreach (Element element in Elements())
        {
            Assert.That(ElementTable.Of(SkillElement.Weapon, element), Is.EqualTo(element));
        }
    }

    [Test]
    public void Percent_EveryCell_IsAMultipleOf25From0To150()
    {
        foreach (Element attack in Elements())
        {
            foreach (Element defense in Elements())
            {
                int percent = ElementTable.Percent(attack, defense);

                Assert.That(percent % 25, Is.Zero, $"{attack} on {defense}");
                Assert.That(percent, Is.InRange(0, 150), $"{attack} on {defense}");
            }
        }
    }

    [Test]
    public void Percent_ExactlySixCells_AreZero()
    {
        (Element, Element)[] zeros = Elements()
            .SelectMany(attack => Elements().Select(defense => (attack, defense)))
            .Where(pair => ElementTable.Percent(pair.attack, pair.defense) == 0)
            .ToArray();

        Assert.That(
            zeros,
            Is.EquivalentTo(
                new[]
                {
                    (Element.Poison, Element.Poison), (Element.Holy, Element.Holy),
                    (Element.Shadow, Element.Shadow), (Element.Undead, Element.Undead),
                    (Element.Shadow, Element.Undead), (Element.Undead, Element.Shadow)
                }));
    }

    [Test]
    public void Percent_ForEveryPair_IsTheTable()
    {
        foreach (Element attack in Elements())
        {
            foreach (Element defense in Elements())
            {
                Assert.That(
                    ElementTable.Percent(attack, defense),
                    Is.EqualTo(Table[(int)attack, (int)defense]),
                    $"{attack} on {defense}");
            }
        }
    }

    [Test]
    public void Percent_TheNeutralRow_HasNoZero()
    {
        Assert.That(Elements().Select(defense => ElementTable.Percent(Element.Neutral, defense)), Has.None.Zero);
    }
}
}
