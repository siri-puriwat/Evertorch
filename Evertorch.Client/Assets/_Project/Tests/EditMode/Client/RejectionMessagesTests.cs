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

    [Test]
    public void Describe_AReasonThisClientDoesNotKnow_StillSaysTheServerRefused()
    {
        string text = RejectionMessages.Describe((CommandRejectionReason)200);

        Assert.That(text, Is.EqualTo("The server refused that."));
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
