using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class CombatEventTests
{
    private static readonly byte[] AttackStartedBytes =
    {
        0x08, 0x80,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x10, 0x00, 0x00, 0x00,
        0xAC, 0x03, 0x00, 0x00,
        0xD6, 0x01, 0x00, 0x00,
        0xD6, 0x01, 0x00, 0x00,
        0xEB, 0x00, 0x00, 0x00
    };

    private static readonly byte[] DamageBytes =
    {
        0x09, 0x80,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03,
        0x44, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00,
        0x28, 0x00
    };

    private static readonly byte[] DiedBytes =
    {
        0x0A, 0x80,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00
    };

    private static readonly byte[] HealthBytes =
    {
        0x14, 0x80, 0x3C, 0x00, 0x00, 0x00, 0x44, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x18, 0x00, 0x00, 0x00
    };

    private static readonly byte[] RevivedBytes =
    {
        0x15, 0x80,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x30, 0x00, 0x00, 0x00
    };

    private static readonly AttackTiming Timing = new(
        TimeSpan.FromMilliseconds(940),
        TimeSpan.FromMilliseconds(470),
        TimeSpan.FromMilliseconds(470),
        TimeSpan.FromMilliseconds(235));

    private static byte[] Write(int length, Func<byte[], int> write)
    {
        byte[] buffer = new byte[length];
        Assert.That(write(buffer), Is.EqualTo(length));
        return buffer;
    }

    private static void AssertStrict(byte[] golden, Func<byte[], bool> tryRead)
    {
        Assert.That(tryRead(golden), Is.True);
        WireMatrix.AssertRejectsEveryTruncation(golden, tryRead);
        WireMatrix.AssertRejectsTrailingData(golden, tryRead);
        WireMatrix.AssertRejectsOtherOpcodes(golden, tryRead);
    }

    [TestCase(0x02, 0x44, false)]
    [TestCase(0x02, 0x00, true)]
    [TestCase(0x01, 0x00, false)]
    [TestCase(0x03, 0x00, false)]
    [TestCase(0x00, 0x44, false)]
    [TestCase(0x04, 0x44, false)]
    public void Damage_ResultAndAmount_MustAgree(byte result, byte amount, bool expected)
    {
        byte[] bytes = WireMatrix.With(WireMatrix.With(DamageBytes, 18, result), 19, amount, 0x00, 0x00, 0x00);

        Assert.That(Damage.TryRead(bytes, out _), Is.EqualTo(expected));
    }

    [TestCase(50, 50, 1000)]
    [TestCase(0, 50, 0)]
    [TestCase(-5, 50, 0)]
    [TestCase(1, 50, 20)]
    [TestCase(1, 10000, 1)]
    [TestCase(49, 50, 980)]
    [TestCase(9999, 10000, 1000)]
    [TestCase(2, 3, 667)]
    public void HealthRatio_RoundsUpSoALiveMonsterNeverShowsEmpty(int current, int maximum, int expected)
    {
        Assert.That(HealthRatio.ToPermille(current, maximum), Is.EqualTo(expected));
    }

    [Test]
    public void AttackStarted_ForGoldenBytes_RoundTrips()
    {
        var message = new AttackStarted(new EntityId(1), new EntityId(42), 16, Timing);

        byte[] written = Write(AttackStarted.EncodedLength, buffer => message.Write(buffer));
        bool isRead = AttackStarted.TryRead(AttackStartedBytes, out AttackStarted read);

        Assert.That(written, Is.EqualTo(AttackStartedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Timing, Is.EqualTo(Timing));
        Assert.That(read.StartTick, Is.EqualTo(16u));
        AssertStrict(AttackStartedBytes, bytes => AttackStarted.TryRead(bytes, out _));
    }

    [Test]
    public void AttackStarted_WhenImpactFallsAfterTheIntervalOrThereIsNoAttacker_IsRefused()
    {
        byte[] lateImpact = WireMatrix.With(AttackStartedBytes, 30, 0xAD, 0x03, 0x00, 0x00);
        byte[] noAttacker = WireMatrix.With(AttackStartedBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] unknownTarget = WireMatrix.With(AttackStartedBytes, 10, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(AttackStarted.TryRead(lateImpact, out _), Is.False);
        Assert.That(AttackStarted.TryRead(noAttacker, out _), Is.False);
        Assert.That(AttackStarted.TryRead(unknownTarget, out _), Is.True, "a target the receiver cannot see is 0");
    }

    [Test]
    public void CharacterHealth_ForGoldenBytes_RoundTripsAndRefusesImpossibleValues()
    {
        byte[] written = Write(
            CharacterHealth.EncodedLength,
            buffer => new CharacterHealth(60, 68, 20, 24).Write(buffer));
        byte[] aboveMaximum = WireMatrix.With(HealthBytes, 2, 0x45, 0x00, 0x00, 0x00);
        byte[] zeroMaximum = WireMatrix.With(HealthBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] spAboveMaximum = WireMatrix.With(HealthBytes, 10, 0x19, 0x00, 0x00, 0x00);
        byte[] noSp = WireMatrix.With(HealthBytes, 10, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(written, Is.EqualTo(HealthBytes));
        AssertStrict(HealthBytes, bytes => CharacterHealth.TryRead(bytes, out _));
        Assert.That(CharacterHealth.TryRead(aboveMaximum, out _), Is.False);
        Assert.That(CharacterHealth.TryRead(zeroMaximum, out _), Is.False);
        Assert.That(CharacterHealth.TryRead(spAboveMaximum, out _), Is.False);
        Assert.That(CharacterHealth.TryRead(noSp, out CharacterHealth read), Is.True, "a job may have no SP");
        Assert.That(read.MaximumSpirit, Is.Zero);
    }

    [Test]
    public void Damage_ForGoldenBytes_RoundTrips()
    {
        var message = new Damage(new EntityId(1), new EntityId(42), CombatResult.Critical, 68, 32, 40);

        byte[] written = Write(Damage.EncodedLength, buffer => message.Write(buffer));
        bool isRead = Damage.TryRead(DamageBytes, out Damage read);

        Assert.That(written, Is.EqualTo(DamageBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Result, Is.EqualTo(CombatResult.Critical));
        Assert.That(read.Amount, Is.EqualTo(68u));
        Assert.That(read.TargetHealthPermille, Is.EqualTo(40));
        AssertStrict(DamageBytes, bytes => Damage.TryRead(bytes, out _));
    }

    [Test]
    public void Damage_WhenTheRatioIsAboveFullOrThereIsNoTarget_IsRefused()
    {
        byte[] aboveFull = WireMatrix.With(DamageBytes, 27, 0xE9, 0x03);
        byte[] noTarget = WireMatrix.With(DamageBytes, 10, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] unknownSource = WireMatrix.With(DamageBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(Damage.TryRead(aboveFull, out _), Is.False);
        Assert.That(Damage.TryRead(noTarget, out _), Is.False);
        Assert.That(Damage.TryRead(unknownSource, out _), Is.True);
    }

    [Test]
    public void EntityDied_ForGoldenBytes_RoundTripsAndNeedsAnEntity()
    {
        byte[] written = Write(
            EntityDied.EncodedLength,
            buffer => new EntityDied(new EntityId(42), new EntityId(1), 32).Write(buffer));
        byte[] noEntity = WireMatrix.With(DiedBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(written, Is.EqualTo(DiedBytes));
        AssertStrict(DiedBytes, bytes => EntityDied.TryRead(bytes, out _));
        Assert.That(EntityDied.TryRead(noEntity, out _), Is.False);
    }

    [Test]
    public void EntityRevived_ForGoldenBytes_RoundTrips()
    {
        var message = new EntityRevived(
            new EntityId(1),
            new WorldPosition(1f, 0.5f, -2f),
            new WorldDirection(0f, 1f),
            48);

        byte[] written = Write(EntityRevived.EncodedLength, buffer => message.Write(buffer));
        byte[] notANumber = WireMatrix.With(RevivedBytes, 10, 0x00, 0x00, 0xC0, 0x7F);

        Assert.That(written, Is.EqualTo(RevivedBytes));
        AssertStrict(RevivedBytes, bytes => EntityRevived.TryRead(bytes, out _));
        Assert.That(EntityRevived.TryRead(notANumber, out _), Is.False);
    }
}
}
