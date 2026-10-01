using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class ChatTextTests
{
    [TestCase(" hi ", true)]
    [TestCase("<b>bold</b> & ~", true)]
    [TestCase(" !\"#$%&'()*+,-./0123456789:;<=>?@AZ[\\]^_`az{|}~", true)]
    [TestCase(" ", false)]
    [TestCase("   ", false)]
    [TestCase("a\tb", false)]
    [TestCase("a\nb", false)]
    [TestCase("a\rb", false)]
    [TestCase("a\u007Fb", false)]
    [TestCase("caf\u00E9", false)]
    [TestCase("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", false)]
    [TestCase("\uD83D\uDE00", false)]
    public void IsValid_TakesPrintableAsciiThatIsNotOnlySpaces(string text, bool isValid)
    {
        Assert.That(ChatText.IsValid(text), Is.EqualTo(isValid));
    }

    [Test]
    public void IsValid_AtTheLengthBounds()
    {
        Assert.That(ChatText.IsValid("a"), Is.True);
        Assert.That(ChatText.IsValid(new string('a', ChatText.MaxLength)), Is.True);
        Assert.That(ChatText.IsValid(new string('a', ChatText.MaxLength + 1)), Is.False);
        Assert.That(ChatText.IsValid(string.Empty), Is.False);
        Assert.That(ChatText.IsValid(null), Is.False);
    }
}
}
