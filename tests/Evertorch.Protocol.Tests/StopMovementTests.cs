using System;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class StopMovementTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x04, 0x00,
        0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A,
    };

    private static readonly StopMovement Golden = new StopMovement(0x01020304, 0x0A0B0C0D);

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[StopMovement.EncodedLength];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(StopMovement.EncodedLength));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = StopMovement.TryRead(GoldenBytes, out StopMovement message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Sequence, Is.EqualTo(0x01020304u));
        Assert.That(message.ClientTick, Is.EqualTo(0x0A0B0C0Du));
    }

    [TestCase(0u, 0u)]
    [TestCase(uint.MaxValue, uint.MaxValue)]
    public void TryRead_AfterWrite_RoundTrips(uint sequence, uint clientTick)
    {
        byte[] buffer = new byte[StopMovement.EncodedLength];
        new StopMovement(sequence, clientTick).Write(buffer);

        bool isRead = StopMovement.TryRead(buffer, out StopMovement message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Sequence, Is.EqualTo(sequence));
        Assert.That(message.ClientTick, Is.EqualTo(clientTick));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => StopMovement.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => StopMovement.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => StopMovement.TryRead(bytes, out _));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[StopMovement.EncodedLength - 1]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
