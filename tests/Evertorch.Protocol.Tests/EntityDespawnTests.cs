using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class EntityDespawnTests
{
    private const int ReasonOffset = 10;

    private static readonly byte[] GoldenBytes =
    {
        0x05, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x02
    };

    private static readonly EntityDespawn Golden = new(new EntityId(0x0123456789ABCDEF), DespawnReason.Removed);

    [TestCase(DespawnReason.OutOfRange)]
    [TestCase(DespawnReason.Removed)]
    [TestCase(DespawnReason.PickedUp)]
    public void TryRead_AfterWrite_RoundTripsReason(DespawnReason reason)
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(-7), reason).Write(buffer);

        bool isRead = EntityDespawn.TryRead(buffer, out EntityDespawn message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Reason, Is.EqualTo(reason));
        Assert.That(message.Entity.Value, Is.EqualTo(-7));
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(255)]
    public void TryRead_WhenReasonIsUnknown_ReturnsFalse(byte reason)
    {
        Assert.That(EntityDespawn.TryRead(WireMatrix.With(GoldenBytes, ReasonOffset, reason), out _), Is.False);
    }

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = EntityDespawn.TryRead(GoldenBytes, out EntityDespawn message);

        Assert.That(isRead, Is.True);
        Assert.That(message.Entity, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.Reason, Is.EqualTo(DespawnReason.Removed));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => EntityDespawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => EntityDespawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => EntityDespawn.TryRead(bytes, out _));
    }

    [Test]
    public void Write_ForKnownMessage_ProducesGoldenBytes()
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];

        int written = Golden.Write(buffer);

        Assert.That(written, Is.EqualTo(EntityDespawn.EncodedLength));
        Assert.That(buffer, Is.EqualTo(GoldenBytes));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[EntityDespawn.EncodedLength - 1]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
