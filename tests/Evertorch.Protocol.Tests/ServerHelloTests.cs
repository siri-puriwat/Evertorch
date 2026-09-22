using System;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ServerHelloTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x01, 0x80,
        0x01, 0x00,
        0x05, 0x00, 0x30, 0x2E, 0x32, 0x2E, 0x30,
        0xD1, 0x6B, 0x32, 0x11,
        0x14, 0x00, 0x00, 0x00,
        0xE6, 0xD5, 0xC4, 0xB3, 0xA2, 0x01, 0x00, 0x00
    };

    private static ServerHello Golden => new(1, "0.2.0", 0x11326BD1, 20, 0x000001A2B3C4D5E6);

    [Test]
    public void Constructor_WithNullBuildVersion_Throws()
    {
        Action create = () => _ = new ServerHello(1, null!, 1, 20, 0);

        Assert.That(create, Throws.ArgumentNullException);
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = ServerHello.TryRead(GoldenBytes, out ServerHello? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.ProtocolVersion, Is.EqualTo(1));
        Assert.That(message.ServerBuildVersion, Is.EqualTo("0.2.0"));
        Assert.That(message.RequiredClientContentVersion, Is.EqualTo(0x11326BD1u));
        Assert.That(message.ServerTickRate, Is.EqualTo(20u));
        Assert.That(message.ServerTimeUnixMilliseconds, Is.EqualTo(0x000001A2B3C4D5E6));
    }

    [Test]
    public void TryRead_WhenBuildVersionLengthExceedsLimit_ReturnsFalse()
    {
        byte[] bytes = new byte[2 + 2 + 2 + 33 + 4 + 4 + 8];
        bytes[0] = 0x01;
        bytes[1] = 0x80;
        bytes[4] = 33;
        bytes[6 + 33 + 4] = 20;

        Assert.That(ServerHello.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => ServerHello.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTickRateIsZero_ReturnsFalse()
    {
        byte[] zeroRate = WireMatrix.With(GoldenBytes, 15, 0x00);

        Assert.That(ServerHello.TryRead(zeroRate, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => ServerHello.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => ServerHello.TryRead(bytes, out _));
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
}
}
