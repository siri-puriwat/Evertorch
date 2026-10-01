using System;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's own words for every refused command (Prototype Content §2).
/// </summary>
[TestFixture]
public sealed class RejectionMessagesTests
{
    [TestCase(CommandRejectionReason.NotEnoughCoins, "You do not have enough coins.")]
    [TestCase(CommandRejectionReason.CoinCapReached, "You cannot hold any more coins.")]
    [TestCase(CommandRejectionReason.NotEnoughPoints, "You do not have enough points.")]
    [TestCase(CommandRejectionReason.RequirementNotMet, "That cannot be done.")]
    public void Describe_TheShopsAndTheBuildsRefusals_InTheClientsWords(CommandRejectionReason reason, string expected)
    {
        Assert.That(RejectionMessages.Describe(reason), Is.EqualTo(expected));
    }

    // A party command's refusal is worded by the command and the name it carried (Prototype Content §2); reason 7
    // means the party's last change is in flight, where for a pickup it means the drop is being picked up.
    [TestCase(PartyCommand.Invite, CommandRejectionReason.InvalidTarget, "Bobby is not online.")]
    [TestCase(PartyCommand.Invite, CommandRejectionReason.NotAllowedNow, "Bobby cannot be invited now.")]
    [TestCase(
        PartyCommand.Invite,
        CommandRejectionReason.RequirementNotMet,
        "Bobby cannot join: your party is full or holds a character of the same account.")]
    [TestCase(PartyCommand.Reply, CommandRejectionReason.InvalidTarget, "Bobby's invite is no longer open.")]
    [TestCase(PartyCommand.Reply, CommandRejectionReason.NotAllowedNow, "You cannot join Bobby's party now.")]
    [TestCase(
        PartyCommand.Reply,
        CommandRejectionReason.RequirementNotMet,
        "Bobby's party is full, or holds a character of your account.")]
    [TestCase(PartyCommand.Leave, CommandRejectionReason.NotAllowedNow, "You are not in a party.")]
    [TestCase(PartyCommand.Kick, CommandRejectionReason.InvalidTarget, "Bobby is not in your party.")]
    [TestCase(PartyCommand.Lead, CommandRejectionReason.NotAllowedNow, "Only the party's leader can do that.")]
    [TestCase(
        PartyCommand.Kick,
        CommandRejectionReason.Busy,
        "Your party's last change is still going through. Try again in a moment.")]
    [TestCase(
        PartyCommand.Invite,
        CommandRejectionReason.ServiceUnavailable,
        "The server cannot save right now. Try again in a moment.")]
    public void DescribeParty_WordsTheRefusalByItsCommandAndName(
        PartyCommand command,
        CommandRejectionReason reason,
        string expected)
    {
        Assert.That(RejectionMessages.DescribeParty(command, "Bobby", reason), Is.EqualTo(expected));
    }

    [Test]
    public void Describe_AReasonThisClientDoesNotKnow_StillSaysTheServerRefused()
    {
        string text = RejectionMessages.Describe((CommandRejectionReason)200);

        Assert.That(text, Is.EqualTo("The server refused that."));
    }

    // An equip refused for a requirement is a weapon the job cannot wield (Gameplay Systems §11.1).
    [Test]
    public void Describe_ARequirementAnEquipDidNotMeet_SaysTheJobCannotWieldIt()
    {
        Assert.That(
            RejectionMessages.Describe(CommandRejectionReason.RequirementNotMet, true),
            Is.EqualTo("Your job cannot wield that."));
        Assert.That(
            RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow, true),
            Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow)),
            "any other reason keeps its words");
    }

    [Test]
    public void Describe_EverySentReason_HasItsOwnWordsNotItsCodeName()
    {
        CommandRejectionReason[] reasons = Enum.GetValues(typeof(CommandRejectionReason))
            .Cast<CommandRejectionReason>()
            .Where(reason => reason != CommandRejectionReason.None)
            .ToArray();
        string[] texts = reasons.Select(RejectionMessages.Describe).ToArray();

        Assert.That(texts, Is.Unique);
        Assert.That(texts, Has.None.EqualTo(RejectionMessages.Describe(CommandRejectionReason.None)));
        for (int index = 0; index < reasons.Length; index++)
        {
            Assert.That(texts[index], Is.Not.Empty);
            Assert.That(texts[index], Does.Not.Contain(reasons[index].ToString()));
        }
    }
}
}
