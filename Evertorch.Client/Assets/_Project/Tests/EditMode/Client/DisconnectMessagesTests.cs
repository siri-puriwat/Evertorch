using System;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's own words for every way a connection ends, and when connecting again can help (Network Protocol
///     §5).
/// </summary>
[TestFixture]
public sealed class DisconnectMessagesTests
{
    private static DisconnectReason[] SentReasons()
    {
        return Enum.GetValues(typeof(DisconnectReason))
            .Cast<DisconnectReason>()
            .Where(reason => reason != DisconnectReason.None)
            .ToArray();
    }

    [TestCase(DisconnectReason.ProtocolMismatch, false)]
    [TestCase(DisconnectReason.ClientBuildUnsupported, false)]
    [TestCase(DisconnectReason.ContentUpdateRequired, false)]
    [TestCase(DisconnectReason.AuthenticationFailed, false)]
    [TestCase(DisconnectReason.SessionExpired, true)]
    [TestCase(DisconnectReason.SessionReplaced, true)]
    [TestCase(DisconnectReason.ServerFull, true)]
    [TestCase(DisconnectReason.ServerNotReady, true)]
    [TestCase(DisconnectReason.RateLimited, true)]
    [TestCase(DisconnectReason.Maintenance, true)]
    [TestCase(DisconnectReason.Kicked, true)]
    [TestCase(DisconnectReason.InternalError, true)]
    public void CanReconnect_OnlyWhereConnectingAgainCanHelp(DisconnectReason reason, bool expected)
    {
        Assert.That(DisconnectMessages.CanReconnect(new DisconnectNotice(reason, string.Empty)), Is.EqualTo(expected));
    }

    [Test]
    public void CanReconnect_WithoutANotice_IsOffered()
    {
        Assert.That(DisconnectMessages.CanReconnect(null), Is.True);
    }

    [Test]
    public void Describe_WithABlankOperatorMessage_AddsNothing()
    {
        string text = DisconnectMessages.Describe(
            new DisconnectNotice(DisconnectReason.Maintenance, "   "),
            TransportDisconnectCause.ClosedByServer);

        Assert.That(text, Is.EqualTo(DisconnectMessages.ForReason(DisconnectReason.Maintenance)));
    }

    [Test]
    public void Describe_WithAnOperatorMessage_ShowsItAfterTheClientsOwnText()
    {
        string text = DisconnectMessages.Describe(
            new DisconnectNotice(DisconnectReason.Maintenance, "Back at 18:00 UTC."),
            TransportDisconnectCause.ClosedByServer);

        Assert.That(text, Does.StartWith(DisconnectMessages.ForReason(DisconnectReason.Maintenance)));
        Assert.That(text, Does.EndWith("Back at 18:00 UTC."));
    }

    [Test]
    public void Describe_WithoutANotice_TellsTheTransportsCause()
    {
        string text = DisconnectMessages.Describe(null, TransportDisconnectCause.TimedOut);

        Assert.That(text, Is.EqualTo(DisconnectMessages.ForCause(TransportDisconnectCause.TimedOut)));
    }

    [Test]
    public void ForCause_EveryCause_HasItsOwnWords()
    {
        string[] texts = Enum.GetValues(typeof(TransportDisconnectCause))
            .Cast<TransportDisconnectCause>()
            .Where(cause => cause != TransportDisconnectCause.None && cause != TransportDisconnectCause.Other)
            .Select(DisconnectMessages.ForCause)
            .ToArray();

        Assert.That(texts, Has.All.Not.Empty);
        Assert.That(texts, Is.Unique);
    }

    [Test]
    public void ForReason_EverySentReason_HasItsOwnWordsNotItsCodeName()
    {
        DisconnectReason[] reasons = SentReasons();
        string[] texts = reasons.Select(DisconnectMessages.ForReason).ToArray();

        Assert.That(texts, Is.Unique);
        for (int index = 0; index < reasons.Length; index++)
        {
            Assert.That(texts[index], Is.Not.Empty);
            Assert.That(texts[index], Does.Not.Contain(reasons[index].ToString()));
        }
    }
}
}
