using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class SharedTargetEntityTests
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
    public void TryRead_AfterWrite_RoundTripsTarget()
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength];
        new TargetEntity(new EntityId(long.MinValue)).Write(buffer);

        bool isRead = TargetEntity.TryRead(buffer, out TargetEntity message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Target, Is.EqualTo(new EntityId(long.MinValue)));
    }

    [Test]
    public void TargetChanged_WriteAndRead_MatchGoldenBytes()
    {
        byte[] golden =
        {
            0x07, 0x80,
            0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
            0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        byte[] buffer = new byte[TargetChanged.EncodedLength];
        new TargetChanged(GoldenTarget, new EntityId(42)).Write(buffer);

        bool isRead = TargetChanged.TryRead(golden, out TargetChanged read);

        Assert.That(buffer, Is.EqualTo(golden));
        Assert.That(isRead, Is.True);
        Assert.That(read.Actor, Is.EqualTo(GoldenTarget));
        Assert.That(read.Target, Is.EqualTo(new EntityId(42)));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        byte[] buffer = new byte[GoldenBytes.Length + 1];
        GoldenBytes.CopyTo(buffer, 0);

        Assert.That(TargetEntity.TryRead(buffer, out _), Is.False);
    }
}
}
