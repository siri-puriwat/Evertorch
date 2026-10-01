using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The chat input's commands (Prototype Content §2): what each typed line sends, and what the client answers
///     itself rather than send a line the server would refuse or read as malformed.
/// </summary>
[TestFixture]
public sealed class ChatCommandsTests
{
    private static (ChatChannel, string, string, string?) Parse(string typed, string? lastWhisperer = null)
    {
        ChatRequest request = ChatCommands.Parse(typed, lastWhisperer, "Anna");
        return (request.Channel, request.Recipient, request.Text, request.Refusal);
    }

    [TestCase("/w", "Whisper with /w Name text.")]
    [TestCase("/w  ", "Whisper with /w Name text.")]
    [TestCase("/w Bo hi", "Bo is not online.")]
    [TestCase("/w Bob-by hi", "Bob-by is not online.")]
    [TestCase("/w anna hi", "You cannot whisper to yourself.")]
    [TestCase("/shout hi", ChatCommands.Usage)]
    [TestCase("/", ChatCommands.Usage)]
    public void Commands_TheServerWouldRefuseOrNotRead_AreAnsweredByTheClient(string typed, string words)
    {
        (ChatChannel channel, string _, string _, string? refusal) = Parse(typed);

        Assert.That(channel, Is.EqualTo(ChatChannel.None), "nothing is sent");
        Assert.That(refusal, Is.EqualTo(words));
    }

    [TestCase("   ")]
    [TestCase("/p   ")]
    [TestCase("/w Bobby")]
    [TestCase("/w Bobby    ")]
    public void ALineWithNothingToSay_SendsNothing_AndSaysNothing(string typed)
    {
        Assert.That(Parse(typed), Is.EqualTo((ChatChannel.None, "", "", (string?)null)));
    }

    [Test]
    public void DescribeRefusal_NamesTheWhisperThatFoundNoOne_AndThePartyThatIsNot()
    {
        Assert.That(
            ChatPanel.DescribeRefusal(ChatChannel.Whisper, "Bobby", CommandRejectionReason.InvalidTarget),
            Is.EqualTo("Bobby is not online."));
        Assert.That(
            ChatPanel.DescribeRefusal(ChatChannel.Party, "", CommandRejectionReason.NotAllowedNow),
            Is.EqualTo("You are not in a party."));
        Assert.That(
            ChatPanel.DescribeRefusal(ChatChannel.Nearby, "", CommandRejectionReason.NotAllowedNow),
            Is.EqualTo("You cannot do that right now."));
    }

    [Test]
    public void PlainText_IsSaidNearby_AsTyped()
    {
        Assert.That(Parse(" hello  all "), Is.EqualTo((ChatChannel.Nearby, "", " hello  all ", (string?)null)));
    }

    [Test]
    public void SlashP_SpeaksToTheParty()
    {
        Assert.That(Parse("/p on my way"), Is.EqualTo((ChatChannel.Party, "", "on my way", (string?)null)));
        Assert.That(Parse("/P ok"), Is.EqualTo((ChatChannel.Party, "", "ok", (string?)null)));
    }

    [Test]
    public void SlashR_AnswersTheLastWhisper_OrSaysNoOneHasWhispered()
    {
        Assert.That(Parse("/r thanks", "Bobby"), Is.EqualTo((ChatChannel.Whisper, "Bobby", "thanks", (string?)null)));
        Assert.That(Parse("/r thanks").Item4, Is.EqualTo("No one has whispered to you."));
    }

    [Test]
    public void SlashW_WhispersTheRestToTheNamedCharacter()
    {
        Assert.That(
            Parse("/w Bobby see you at the gate"),
            Is.EqualTo((ChatChannel.Whisper, "Bobby", "see you at the gate", (string?)null)));
    }
}
}
