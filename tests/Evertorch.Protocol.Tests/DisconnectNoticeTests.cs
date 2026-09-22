using System;
using System.Linq;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class DisconnectNoticeTests
{
    private const int ReasonOffset = 2;

    private static readonly byte[] GoldenBytes =
    {
        0x13, 0x80,
        0x03,
        0x06, 0x00, 0x55, 0x70, 0x64, 0x61, 0x74, 0x65
    };

    private static DisconnectNotice Golden => new(DisconnectReason.ContentUpdateRequired, "Update");

    private static DisconnectReason[] SendableReasons => ((DisconnectReason[])Enum.GetValues(typeof(DisconnectReason)))
        .Where(reason => reason != DisconnectReason.None)
        .ToArray();

    [TestCaseSource(nameof(SendableReasons))]
    public void TryRead_AfterWrite_RoundTripsEveryReasonWithAnEmptyMessage(DisconnectReason reason)
    {
        var original = new DisconnectNotice(reason, string.Empty);
        byte[] buffer = new byte[original.GetEncodedLength()];
        original.Write(buffer);

        bool isRead = DisconnectNotice.TryRead(buffer, out DisconnectNotice? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Reason, Is.EqualTo(reason));
        Assert.That(message.Message, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(13)]
    [TestCase(255)]
    public void TryRead_WhenReasonIsUnknown_ReturnsFalse(byte reason)
    {
        Assert.That(DisconnectNotice.TryRead(WireMatrix.With(GoldenBytes, ReasonOffset, reason), out _), Is.False);
    }

    [Test]
    public void DisconnectReason_Values_KeepTheirStableNumbers()
    {
        string[] expected =
        {
            "None=0", "ProtocolMismatch=1", "ClientBuildUnsupported=2", "ContentUpdateRequired=3",
            "AuthenticationFailed=4", "SessionExpired=5", "SessionReplaced=6", "ServerFull=7", "ServerNotReady=8",
            "RateLimited=9", "Maintenance=10", "Kicked=11", "InternalError=12"
        };

        string[] actual = ((DisconnectReason[])Enum.GetValues(typeof(DisconnectReason)))
            .Select(reason => reason + "=" + (byte)reason)
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = DisconnectNotice.TryRead(GoldenBytes, out DisconnectNotice? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Reason, Is.EqualTo(DisconnectReason.ContentUpdateRequired));
        Assert.That(message.Message, Is.EqualTo("Update"));
    }

    [Test]
    public void TryRead_WhenMessageExceedsLimit_ReturnsFalse()
    {
        byte[] bytes = new byte[2 + 1 + 2 + 129];
        bytes[0] = 0x13;
        bytes[1] = 0x80;
        bytes[2] = 0x01;
        bytes[3] = 129;

        Assert.That(DisconnectNotice.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => DisconnectNotice.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => DisconnectNotice.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => DisconnectNotice.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[Golden.GetEncodedLength()];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(GoldenBytes.Length));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[GoldenBytes.Length - 1]);

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_WhenMessageExceedsLimit_Throws()
    {
        var message = new DisconnectNotice(DisconnectReason.Kicked, new string('x', 129));
        Action write = () => message.Write(new byte[512]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
