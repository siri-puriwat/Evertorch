using System;
using System.Text;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ClientHelloTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x01, 0x00,
        0x01, 0x00,
        0x05, 0x00, 0x30, 0x2E, 0x32, 0x2E, 0x30,
        0xD1, 0x6B, 0x32, 0x11,
        0x07, 0x00, 0x64, 0x65, 0x76, 0x3A, 0x61, 0x6E, 0x6E
    };

    private static ClientHello Golden => new(1, "0.2.0", 0x11326BD1, "dev:ann");

    [TestCase("", "")]
    [TestCase("версия-1", "токен")]
    [TestCase("1.0.0+build.20260920", "dev:someone with spaces")]
    public void TryRead_AfterWrite_RoundTripsStrings(string buildVersion, string token)
    {
        var original = new ClientHello(ushort.MaxValue, buildVersion, uint.MaxValue, token);
        byte[] buffer = new byte[original.GetEncodedLength()];
        original.Write(buffer);

        bool isRead = ClientHello.TryRead(buffer, out ClientHello? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.ProtocolVersion, Is.EqualTo(ushort.MaxValue));
        Assert.That(message.ClientBuildVersion, Is.EqualTo(buildVersion));
        Assert.That(message.ClientContentVersion, Is.EqualTo(uint.MaxValue));
        Assert.That(message.SessionToken, Is.EqualTo(token));
    }

    // Well-formed in every other respect, so only the length limit can be what rejects them.
    [TestCase(33, 1)]
    [TestCase(1, 513)]
    public void TryRead_WhenStringExceedsLimit_ReturnsFalse(int buildVersionLength, int tokenLength)
    {
        byte[] oversized = EncodeWithoutLimits(buildVersionLength, tokenLength);

        Assert.That(ClientHello.TryRead(oversized, out _), Is.False);
    }

    [TestCase(33, 1)]
    [TestCase(1, 513)]
    public void Write_WhenStringExceedsLimit_Throws(int buildVersionLength, int tokenLength)
    {
        var message = new ClientHello(1, new string('v', buildVersionLength), 1, new string('t', tokenLength));
        Action write = () => message.Write(new byte[2048]);

        Assert.That(write, Throws.ArgumentException);
    }

    private static byte[] EncodeWithoutLimits(int buildVersionLength, int tokenLength)
    {
        byte[] bytes = new byte[2 + 2 + 2 + buildVersionLength + 4 + 2 + tokenLength];
        bytes[0] = 0x01;
        bytes[2] = 0x01;
        bytes[4] = (byte)(buildVersionLength & 0xFF);
        bytes[5] = (byte)(buildVersionLength >> 8);
        int tokenPrefix = 6 + buildVersionLength + 4;
        bytes[tokenPrefix] = (byte)(tokenLength & 0xFF);
        bytes[tokenPrefix + 1] = (byte)(tokenLength >> 8);
        for (int index = 0; index < buildVersionLength; index++)
        {
            bytes[6 + index] = (byte)'v';
        }

        for (int index = 0; index < tokenLength; index++)
        {
            bytes[tokenPrefix + 2 + index] = (byte)'t';
        }

        return bytes;
    }

    private static byte[] Encode(string buildVersion, string token)
    {
        var message = new ClientHello(1, buildVersion, 1, token);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void TryReadProtocolVersion_WhenShorterThanOpcodeAndVersion_ReturnsFalse(int length)
    {
        Assert.That(ClientHello.TryReadProtocolVersion(GoldenBytes.AsSpan(0, length), out _), Is.False);
    }

    [Test]
    public void Constructor_WithNullString_Throws()
    {
        Action nullBuild = () => _ = new ClientHello(1, null!, 1, "t");
        Action nullToken = () => _ = new ClientHello(1, "v", 1, null!);

        Assert.That(nullBuild, Throws.ArgumentNullException);
        Assert.That(nullToken, Throws.ArgumentNullException);
    }

    [Test]
    public void TryReadProtocolVersion_ForGoldenBytes_ReadsTheVersionAlone()
    {
        bool isRead = ClientHello.TryReadProtocolVersion(GoldenBytes, out ushort version);

        Assert.That(isRead, Is.True);
        Assert.That(version, Is.EqualTo(1));
    }

    [Test]
    public void TryReadProtocolVersion_OfALongerHelloInAnotherLayout_StillReadsTheVersion()
    {
        byte[] foreign = new byte[ProtocolLimits.MaxClientPayloadBytes + 100];
        foreign[0] = 0x01;
        foreign[2] = 0x0E;
        foreign[4] = 0xFF;

        bool isRead = ClientHello.TryReadProtocolVersion(foreign, out ushort version);

        Assert.That(isRead, Is.True);
        Assert.That(version, Is.EqualTo(14));
        Assert.That(ClientHello.TryRead(foreign, out _), Is.False, "the full decode still refuses it");
    }

    [Test]
    public void TryReadProtocolVersion_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => ClientHello.TryReadProtocolVersion(bytes, out _));
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = ClientHello.TryRead(GoldenBytes, out ClientHello? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.ProtocolVersion, Is.EqualTo(1));
        Assert.That(message.ClientBuildVersion, Is.EqualTo("0.2.0"));
        Assert.That(message.ClientContentVersion, Is.EqualTo(0x11326BD1u));
        Assert.That(message.SessionToken, Is.EqualTo("dev:ann"));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => ClientHello.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenStringLengthPrefixExceedsRemainingBytes_ReturnsFalse()
    {
        byte[] lying = WireMatrix.With(GoldenBytes, 15, 0xFF, 0x01);

        Assert.That(ClientHello.TryRead(lying, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenStringsAreExactlyAtTheirLimits_ReturnsTrue()
    {
        Assert.That(ClientHello.TryRead(EncodeWithoutLimits(32, 512), out _), Is.True);
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => ClientHello.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => ClientHello.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenUtf8IsInvalid_ReturnsFalse()
    {
        byte[] invalid = WireMatrix.With(GoldenBytes, 17, 0xC3, 0x28);

        Assert.That(ClientHello.TryRead(invalid, out _), Is.False);
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
    public void Write_WhenLimitIsCountedInBytesNotCharacters_Throws()
    {
        var message = new ClientHello(1, new string('я', 17), 1, "t");
        Action write = () => message.Write(new byte[2048]);

        Assert.That(Encoding.UTF8.GetByteCount(message.ClientBuildVersion), Is.EqualTo(34));
        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_WithBothStringsAtTheirLimits_IsTheLargestClientPayload()
    {
        var largest = new ClientHello(1, new string('v', 32), 1, new string('t', 512));

        Assert.That(largest.GetEncodedLength(), Is.EqualTo(ProtocolLimits.MaxClientPayloadBytes));
        Assert.That(ClientHello.TryRead(Encode(new string('v', 32), new string('t', 512)), out _), Is.True);
    }
}
}
