using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class CharacterNamesTests
{
    [TestCase("Abcd", true)]
    [TestCase("abc1", true)]
    [TestCase("ZZ99zz", true)]
    [TestCase("Abcdefghijklmnopqrstuvw", true)]
    [TestCase("Abc", false)]
    [TestCase("Abcdefghijklmnopqrstuvwx", false)]
    [TestCase("", false)]
    [TestCase("Ab cd", false)]
    [TestCase("Ab-cd", false)]
    [TestCase("Ab_cd", false)]
    [TestCase("<b>Ab", false)]
    [TestCase("Abcé", false)]
    [TestCase("Abcก", false)]
    public void IsValid_AcceptsFourToTwentyThreeAsciiLettersAndDigits(string name, bool isValid)
    {
        Assert.That(CharacterNames.IsValid(name), Is.EqualTo(isValid));
    }

    [Test]
    public void IsValid_ForNull_IsFalse()
    {
        Assert.That(CharacterNames.IsValid(null), Is.False);
    }
}
}
