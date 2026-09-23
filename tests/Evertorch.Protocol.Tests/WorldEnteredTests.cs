using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class WorldEnteredTests
{
    private const int PositionXOffset = 32;
    private const int MovementSpeedOffset = 52;
    private const int CurrentHealthOffset = 56;
    private const int MaximumHealthOffset = 60;
    private const int AttackRangeOffset = 64;

    private static readonly byte[] NotANumber = { 0x00, 0x00, 0xC0, 0x7F };
    private static readonly byte[] PositiveInfinity = { 0x00, 0x00, 0x80, 0x7F };
    private static readonly byte[] MinusOne = { 0x00, 0x00, 0x80, 0xBF };

    private static readonly byte[] GoldenBytes =
    {
        0x03, 0x80,
        0x05, 0x00, 0x6D, 0x61, 0x70, 0x2E, 0x61,
        0x01, 0x00, 0x00, 0x00,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x03, 0x02, 0x01, 0x00,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0xA0, 0x40,
        0x44, 0x00, 0x00, 0x00,
        0x44, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xC0, 0x3F
    };

    private static WorldEntered Golden => new(
        new MapDefinitionId("map.a"),
        1,
        new EntityId(0x0123456789ABCDEF),
        new JobDefinitionId("job.a"),
        0x00010203,
        new WorldPosition(1f, 0.5f, -2f),
        new WorldDirection(0f, 1f),
        5f,
        68,
        68,
        1.5f);

    [Test]
    public void TryRead_ForGoldenBytes_ReturnsKnownMessage()
    {
        bool isRead = WorldEntered.TryRead(GoldenBytes, out WorldEntered? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Map, Is.EqualTo(new MapDefinitionId("map.a")));
        Assert.That(message.MapInstance, Is.EqualTo(1u));
        Assert.That(message.LocalEntity, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.Job, Is.EqualTo(new JobDefinitionId("job.a")));
        Assert.That(message.ServerTick, Is.EqualTo(0x00010203u));
        Assert.That(message.Position, Is.EqualTo(new WorldPosition(1f, 0.5f, -2f)));
        Assert.That(message.Facing, Is.EqualTo(new WorldDirection(0f, 1f)));
        Assert.That(message.MovementSpeed, Is.EqualTo(5f));
        Assert.That(message.CurrentHealth, Is.EqualTo(68u));
        Assert.That(message.MaximumHealth, Is.EqualTo(68u));
        Assert.That(message.AttackRange, Is.EqualTo(1.5f));
    }

    [Test]
    public void TryRead_WhenAnyFloatIsNotFinite_ReturnsFalse()
    {
        for (int offset = PositionXOffset; offset <= MovementSpeedOffset; offset += sizeof(float))
        {
            Assert.That(WorldEntered.TryRead(WireMatrix.With(GoldenBytes, offset, NotANumber), out _), Is.False);
            Assert.That(WorldEntered.TryRead(WireMatrix.With(GoldenBytes, offset, PositiveInfinity), out _), Is.False);
        }
    }

    [Test]
    public void TryRead_WhenAttackRangeIsNegativeOrNotFinite_ReturnsFalse()
    {
        Assert.That(WorldEntered.TryRead(WireMatrix.With(GoldenBytes, AttackRangeOffset, MinusOne), out _), Is.False);
        Assert.That(WorldEntered.TryRead(WireMatrix.With(GoldenBytes, AttackRangeOffset, NotANumber), out _), Is.False);
    }

    [Test]
    public void TryRead_WhenHealthIsImpossible_ReturnsFalse()
    {
        byte[] aboveMaximum = WireMatrix.With(GoldenBytes, CurrentHealthOffset, 0x45, 0x00, 0x00, 0x00);
        byte[] zeroMaximum = WireMatrix.With(
            WireMatrix.With(GoldenBytes, CurrentHealthOffset, 0, 0, 0, 0),
            MaximumHealthOffset,
            0,
            0,
            0,
            0);

        Assert.That(WorldEntered.TryRead(aboveMaximum, out _), Is.False);
        Assert.That(WorldEntered.TryRead(zeroMaximum, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenJobIdIsOfAnotherKind_ReturnsFalse()
    {
        byte[] mapId = WireMatrix.With(GoldenBytes, 23, 0x6D, 0x61, 0x70);

        Assert.That(WorldEntered.TryRead(mapId, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenMapIdIsOfAnotherKind_ReturnsFalse()
    {
        byte[] jobId = WireMatrix.With(GoldenBytes, 4, 0x6A, 0x6F, 0x62);

        Assert.That(WorldEntered.TryRead(jobId, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenMovementSpeedIsNegative_ReturnsFalse()
    {
        byte[] negative = WireMatrix.With(GoldenBytes, MovementSpeedOffset, MinusOne);

        Assert.That(WorldEntered.TryRead(negative, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => WorldEntered.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => WorldEntered.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => WorldEntered.TryRead(bytes, out _));
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
    public void Write_WithLongestIds_RoundTrips()
    {
        var longestMap = new MapDefinitionId($"map.{new string('a', 60)}");
        var longestJob = new JobDefinitionId($"job.{new string('b', 60)}");
        var original = new WorldEntered(
            longestMap,
            0,
            default,
            longestJob,
            0,
            default,
            new WorldDirection(1f, 0f),
            0f,
            0,
            1,
            0f);
        byte[] buffer = new byte[original.GetEncodedLength()];
        original.Write(buffer);

        bool isRead = WorldEntered.TryRead(buffer, out WorldEntered? message);

        Assert.That(isRead, Is.True);
        Assert.That(buffer.Length, Is.EqualTo(186), "the largest WorldEntered");
        Assert.That(message!.Map, Is.EqualTo(longestMap));
        Assert.That(message.Job, Is.EqualTo(longestJob));
    }
}
}
