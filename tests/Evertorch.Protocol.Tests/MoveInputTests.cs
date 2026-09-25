using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class MoveInputTests
{
    private const int DirectionXOffset = 10;
    private const int DirectionZOffset = 14;

    private static readonly byte[] GoldenBytes =
    {
        0x03, 0x00,
        0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A,
        0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x00, 0xBF,
        0x2A
    };

    private static readonly MoveInput Golden = new(new MoveIntent(0x01020304, 0x0A0B0C0D, 1f, -0.5f), 0x2A);

    [TestCase(0u, 0u, 0f, 0f)]
    [TestCase(uint.MaxValue, uint.MaxValue, -1f, 1f)]
    [TestCase(7u, 9u, 3.4e38f, -3.4e38f)]
    public void TryRead_AfterWrite_RoundTripsIntent(uint sequence, uint clientTick, float x, float z)
    {
        var intent = new MoveIntent(sequence, clientTick, x, z);
        byte[] buffer = new byte[MoveInput.EncodedLength];
        new MoveInput(intent).Write(buffer);

        bool isRead = MoveInput.TryRead(buffer, out MoveInput message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Intent, Is.EqualTo(intent));
    }

    [TestCase(DirectionXOffset, (byte)0xC0, (byte)0x7F)]
    [TestCase(DirectionZOffset, (byte)0xC0, (byte)0x7F)]
    [TestCase(DirectionXOffset, (byte)0x80, (byte)0x7F)]
    [TestCase(DirectionZOffset, (byte)0x80, (byte)0xFF)]
    public void TryRead_WhenDirectionNonFinite_ReturnsFalse(int offset, byte third, byte fourth)
    {
        byte[] invalid = WireMatrix.With(GoldenBytes, offset, 0x00, 0x00, third, fourth);

        Assert.That(MoveInput.TryRead(invalid, out _), Is.False);
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownIntent()
    {
        bool isRead = MoveInput.TryRead(GoldenBytes, out MoveInput message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Intent, Is.EqualTo(new MoveIntent(0x01020304, 0x0A0B0C0D, 1f, -0.5f)));
        Assert.That(message.MapEpoch, Is.EqualTo(0x2A));
        Assert.That(MoveInput.EncodedLength, Is.EqualTo(19));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => MoveInput.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => MoveInput.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => MoveInput.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[MoveInput.EncodedLength];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(MoveInput.EncodedLength));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[MoveInput.EncodedLength - 1]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
