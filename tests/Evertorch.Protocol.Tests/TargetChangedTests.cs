using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class TargetChangedTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x07, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly TargetChanged Golden = new(new EntityId(0x0123456789ABCDEF), new EntityId(42));

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = TargetChanged.TryRead(GoldenBytes, out TargetChanged message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Actor, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.Target, Is.EqualTo(new EntityId(42)));
    }

    [Test]
    public void TryRead_WhenActorIsZero_ReturnsFalse()
    {
        byte[] noActor = WireMatrix.With(GoldenBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(TargetChanged.TryRead(noActor, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => TargetChanged.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTargetIsZero_MeansNoTarget()
    {
        byte[] cleared = WireMatrix.With(GoldenBytes, 10, 0, 0, 0, 0, 0, 0, 0, 0);

        bool isRead = TargetChanged.TryRead(cleared, out TargetChanged message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Target, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => TargetChanged.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => TargetChanged.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[TargetChanged.EncodedLength];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(GoldenBytes.Length));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        byte[] buffer = new byte[TargetChanged.EncodedLength - 1];

        // C# 9 cannot choose between NUnit 4's TestDelegate and Action overloads for a bare lambda.
        Action write = () => Golden.Write(buffer);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
