using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class TargetEntityTests
{
    private static readonly byte[] GoldenBytes =
    {
        0x05, 0x00,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
    };

    private static readonly EntityId GoldenTarget = new EntityId(0x0123456789ABCDEF);

    [Test]
    public void Write_ForKnownTarget_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength];

        new TargetEntity(GoldenTarget).Write(buffer);

        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownTarget()
    {
        bool isRead = TargetEntity.TryRead(GoldenBytes, out TargetEntity message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Target, Is.EqualTo(GoldenTarget));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void TryRead_AfterWrite_RoundTripsTarget(long target)
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength];
        new TargetEntity(new EntityId(target)).Write(buffer);

        bool isRead = TargetEntity.TryRead(buffer, out TargetEntity message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Target.Value, Is.EqualTo(target));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        byte[] buffer = (byte[])GoldenBytes.Clone();
        buffer[0] = 0x06;

        Assert.That(TargetEntity.TryRead(buffer, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenTruncated_ReturnsFalse()
    {
        ReadOnlySpan<byte> truncated = new ReadOnlySpan<byte>(GoldenBytes, 0, GoldenBytes.Length - 1);

        Assert.That(TargetEntity.TryRead(truncated, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        byte[] buffer = new byte[GoldenBytes.Length + 1];
        GoldenBytes.CopyTo(buffer, 0);

        Assert.That(TargetEntity.TryRead(buffer, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenEmpty_ReturnsFalse()
    {
        Assert.That(TargetEntity.TryRead(ReadOnlySpan<byte>.Empty, out _), Is.False);
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength - 1];

        // C# 9 cannot choose between NUnit 4's TestDelegate and Action overloads for a bare lambda.
        Action write = () => new TargetEntity(GoldenTarget).Write(buffer);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
