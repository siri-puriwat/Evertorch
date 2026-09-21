using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class EntitySpawnTests
{
    private const int KindOffset = 10;
    private const int DefinitionTextOffset = 13;
    private const int PositionXOffset = 18;
    private const int FlagsOffset = 38;

    private static readonly byte[] NotANumber = { 0x00, 0x00, 0xC0, 0x7F };

    private static readonly byte[] GoldenBytes =
    {
        0x04, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x01,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x01, 0x00,
    };

    private static EntitySpawn Golden => new EntitySpawn(
        new EntityId(0x0123456789ABCDEF),
        EntityKind.Player,
        "job.a",
        new WorldPosition(1f, 0.5f, -2f),
        new WorldDirection(0f, 1f),
        EntityStateFlags.Moving);

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
        bool isRead = EntitySpawn.TryRead(GoldenBytes, out EntitySpawn? message);

        Assert.That(isRead, Is.True);
        Assert.That(message!.Entity, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(message.DefinitionId, Is.EqualTo("job.a"));
        Assert.That(message.Position, Is.EqualTo(new WorldPosition(1f, 0.5f, -2f)));
        Assert.That(message.Facing, Is.EqualTo(new WorldDirection(0f, 1f)));
        Assert.That(message.StateFlags, Is.EqualTo(EntityStateFlags.Moving));
    }

    [Test]
    public void TryRead_WhenTruncatedAtAnyLength_ReturnsFalse()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenTrailingDataPresent_ReturnsFalse()
    {
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [Test]
    public void TryRead_WhenOpcodeDiffers_ReturnsFalse()
    {
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, bytes => EntitySpawn.TryRead(bytes, out _));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(255)]
    public void TryRead_WhenKindIsUnknown_ReturnsFalse(byte kind)
    {
        Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, KindOffset, kind), out _), Is.False);
    }

    [TestCase(0x02, 0x00)]
    [TestCase(0x01, 0x80)]
    [TestCase(0xFF, 0xFF)]
    public void TryRead_WhenFlagsContainUnknownBits_ReturnsFalse(byte low, byte high)
    {
        Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, FlagsOffset, low, high), out _), Is.False);
    }

    [Test]
    public void TryRead_WhenDefinitionIsNotAJobForAPlayer_ReturnsFalse()
    {
        byte[] mapId = WireMatrix.With(GoldenBytes, DefinitionTextOffset, 0x6D, 0x61, 0x70);

        Assert.That(EntitySpawn.TryRead(mapId, out _), Is.False);
    }

    [Test]
    public void TryRead_WhenAnyFloatIsNotFinite_ReturnsFalse()
    {
        for (int offset = PositionXOffset; offset < FlagsOffset; offset += sizeof(float))
        {
            Assert.That(EntitySpawn.TryRead(WireMatrix.With(GoldenBytes, offset, NotANumber), out _), Is.False);
        }
    }

    [Test]
    public void Write_WhenDefinitionIdExceedsLimit_Throws()
    {
        EntitySpawn message =
            new EntitySpawn(default, EntityKind.Player, "job." + new string('a', 61), default, default, 0);
        Action write = () => message.Write(new byte[512]);

        Assert.That(write, Throws.ArgumentException);
    }

    [Test]
    public void Write_WhenDestinationTooSmall_Throws()
    {
        Action write = () => Golden.Write(new byte[GoldenBytes.Length - 1]);

        Assert.That(write, Throws.ArgumentException);
    }
}
}
