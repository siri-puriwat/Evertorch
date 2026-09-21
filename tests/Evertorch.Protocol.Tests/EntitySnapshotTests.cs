using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class EntitySnapshotTests
{
    private const int CountOffset = 10;
    private const int FirstStateOffset = 11;
    private const int FirstFlagsOffset = FirstStateOffset + EntityState.EncodedLength - sizeof(ushort);

    private static readonly byte[] NotANumber = { 0x00, 0x00, 0xC0, 0x7F };

    private static readonly byte[] GoldenBytes =
    {
        0x06, 0x80,
        0x03, 0x02, 0x01, 0x00,
        0x04, 0x03, 0x02, 0x01,
        0x02,

        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0xA0, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00,

        0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00,
    };

    private static readonly EntityState MovingState = new EntityState(
        new EntityId(0x0123456789ABCDEF),
        new WorldPosition(1f, 0.5f, -2f),
        new WorldDirection(0f, 1f),
        5f,
        0f,
        0f,
        EntityStateFlags.Moving);

    private static readonly EntityState IdleState = new EntityState(
        new EntityId(2),
        new WorldPosition(0f, 0f, 0f),
        new WorldDirection(1f, 0f),
        0f,
        0f,
        0f,
        EntityStateFlags.None);

    private static EntitySnapshot Golden =>
        new EntitySnapshot(0x00010203, 0x01020304, new[] { MovingState, IdleState });

    [Test]
    public void EncodedLengths_AreTheDocumentedSizes()
    {
        Assert.That(EntityState.EncodedLength, Is.EqualTo(42));
        Assert.That(EntitySnapshot.HeaderLength, Is.EqualTo(11));
        Assert.That(EntitySnapshot.MaxEncodedLength, Is.EqualTo(1019));
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
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = EntitySnapshot.TryRead(GoldenBytes, out EntitySnapshot? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.ServerTick, Is.EqualTo(0x00010203u));
        Assert.That(message.LastProcessedInputSequence, Is.EqualTo(0x01020304u));
        Assert.That(message.Entities, Has.Count.EqualTo(2));
        AssertSameState(message.Entities[0], MovingState);
        AssertSameState(message.Entities[1], IdleState);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(EntitySnapshot.MaxEntities)]
    public void TryRead_AfterWrite_RoundTripsAnyAllowedCount(int count)
    {
        EntityState[] states = Enumerable.Range(1, count)
            .Select(index => new EntityState(new EntityId(index), default, default, index, 0f, -index, 0))
            .ToArray();
        EntitySnapshot original = new EntitySnapshot(9, 8, states);
        byte[] buffer = new byte[original.GetEncodedLength()];
        original.Write(buffer);

        bool isRead = EntitySnapshot.TryRead(buffer, out EntitySnapshot? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Entities.Select(state => state.Entity.Value), Is.EqualTo(Enumerable.Range(1, count)));
    }

    [Test]
    public void Write_WithMaximumEntities_FitsTheDocumentedMaximum()
    {
        EntityState[] states = new EntityState[EntitySnapshot.MaxEntities];
        EntitySnapshot largest = new EntitySnapshot(1, 1, states);

        Assert.That(largest.GetEncodedLength(), Is.EqualTo(EntitySnapshot.MaxEncodedLength));
    }

    [Test]
    public void Constructor_WithTooManyEntities_Throws()
    {
        Action create = () => _ = new EntitySnapshot(1, 1, new EntityState[EntitySnapshot.MaxEntities + 1]);

        Assert.That(create, Throws.ArgumentException);
    }

    [Test]
    public void Constructor_WithNullEntities_Throws()
    {
        Action create = () => _ = new EntitySnapshot(1, 1, null!);

        Assert.That(create, Throws.ArgumentNullException);
    }

    [Test]
    public void TryRead_WhenCountExceedsMaximum_ReturnsFalse()
    {
        byte[] oversized = new byte[EntitySnapshot.HeaderLength + (25 * EntityState.EncodedLength)];
        oversized[0] = 0x06;
        oversized[1] = 0x80;
        oversized[CountOffset] = 25;

        Assert.That(EntitySnapshot.TryRead(oversized, out _), Is.False);
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(255)]
    public void TryRead_WhenCountDisagreesWithLength_ReturnsFalse(byte count)
    {
        Assert.That(EntitySnapshot.TryRead(WireMatrix.With(GoldenBytes, CountOffset, count), out _), Is.False);
    }

    [Test]
    public void TryRead_WhenFlagsContainUnknownBits_ReturnsFalse()
    {
        byte[] invalid = WireMatrix.With(GoldenBytes, FirstFlagsOffset, 0x03, 0x00);

        Assert.That(EntitySnapshot.TryRead(invalid, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenAnyFloatIsNotFinite_ReturnsFalse()
    {
        int firstFloat = FirstStateOffset + sizeof(long);
        for (int offset = firstFloat; offset < FirstFlagsOffset; offset += sizeof(float))
        {
            Assert.That(EntitySnapshot.TryRead(WireMatrix.With(GoldenBytes, offset, NotANumber), out _), Is.False);
        }
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => EntitySnapshot.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => EntitySnapshot.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => EntitySnapshot.TryRead(bytes, out _));
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[GoldenBytes.Length - 1]);

        Assert.That(write, Throws.ArgumentException);
    }

    private static void AssertSameState(EntityState actual, EntityState expected)
    {
        Assert.That(actual.Entity, Is.EqualTo(expected.Entity));
        Assert.That(actual.Position, Is.EqualTo(expected.Position));
        Assert.That(actual.Facing, Is.EqualTo(expected.Facing));
        Assert.That(actual.VelocityX, Is.EqualTo(expected.VelocityX));
        Assert.That(actual.VelocityY, Is.EqualTo(expected.VelocityY));
        Assert.That(actual.VelocityZ, Is.EqualTo(expected.VelocityZ));
        Assert.That(actual.StateFlags, Is.EqualTo(expected.StateFlags));
    }
}
}
