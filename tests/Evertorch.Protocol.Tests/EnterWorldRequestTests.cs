using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class EnterWorldRequestTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x02, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01
    };

    private static readonly EnterWorldRequest Golden = new(new CharacterId(0x0102030405060708));

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void TryRead_AfterWrite_RoundTripsCharacter(long value)
    {
        byte[] buffer = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(value)).Write(buffer);

        bool isRead = EnterWorldRequest.TryRead(buffer, out EnterWorldRequest message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Character.Value, Is.EqualTo(value));
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = EnterWorldRequest.TryRead(GoldenBytes, out EnterWorldRequest message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Character, Is.EqualTo(new CharacterId(0x0102030405060708)));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => EnterWorldRequest.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => EnterWorldRequest.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => EnterWorldRequest.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[EnterWorldRequest.EncodedLength];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(EnterWorldRequest.EncodedLength));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[EnterWorldRequest.EncodedLength - 1]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
