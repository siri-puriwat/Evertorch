using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class CommandMessageTests
{
    private static readonly byte[] AttackBytes =
    {
        0x06, 0x00,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x78, 0x56, 0x34, 0x12
    };

    private static readonly byte[] CancelBytes = { 0x07, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static readonly byte[] RespawnBytes = { 0x0C, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static bool ReadAttack(byte[] bytes)
    {
        return AttackEntity.TryRead(bytes, out _);
    }

    private static bool ReadCancel(byte[] bytes)
    {
        return CancelAction.TryRead(bytes, out _);
    }

    private static bool ReadRespawn(byte[] bytes)
    {
        return Respawn.TryRead(bytes, out _);
    }

    [Test]
    public void AttackEntity_ForGoldenBytes_RoundTrips()
    {
        byte[] buffer = new byte[AttackEntity.EncodedLength];

        int written = new AttackEntity(new EntityId(0x0123456789ABCDEF), 0x12345678).Write(buffer);
        bool isRead = AttackEntity.TryRead(AttackBytes, out AttackEntity message);

        Assert.That(written, Is.EqualTo(14));
        Assert.That(buffer, Is.EqualTo(AttackBytes));
        Assert.That(isRead, Is.True);
        Assert.That(message.Target, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(message.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void AttackEntity_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(AttackBytes, ReadAttack);
        WireMatrix.AssertRejectsTrailingData(AttackBytes, ReadAttack);
        WireMatrix.AssertRejectsOtherOpcodes(AttackBytes, ReadAttack);
    }

    [Test]
    public void CancelAction_ForGoldenBytes_RoundTrips()
    {
        byte[] buffer = new byte[CancelAction.EncodedLength];

        int written = new CancelAction(0x12345678).Write(buffer);
        bool isRead = CancelAction.TryRead(CancelBytes, out CancelAction message);

        Assert.That(written, Is.EqualTo(6));
        Assert.That(buffer, Is.EqualTo(CancelBytes));
        Assert.That(isRead, Is.True);
        Assert.That(message.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void CancelAction_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(CancelBytes, ReadCancel);
        WireMatrix.AssertRejectsTrailingData(CancelBytes, ReadCancel);
        WireMatrix.AssertRejectsOtherOpcodes(CancelBytes, ReadCancel);
    }

    [Test]
    public void Commands_WhenDestinationTooSmall_Throw()
    {
        // C# 9 cannot choose between NUnit 4's TestDelegate and Action overloads for a bare lambda.
        Action attack = () => new AttackEntity(new EntityId(1), 1).Write(new byte[AttackEntity.EncodedLength - 1]);
        Action cancel = () => new CancelAction(1).Write(new byte[CancelAction.EncodedLength - 1]);
        Action respawn = () => new Respawn(1).Write(new byte[Respawn.EncodedLength - 1]);

        Assert.That(attack, Throws.ArgumentException);
        Assert.That(cancel, Throws.ArgumentException);
        Assert.That(respawn, Throws.ArgumentException);
    }

    [Test]
    public void Respawn_ForGoldenBytes_RoundTrips()
    {
        byte[] buffer = new byte[Respawn.EncodedLength];

        int written = new Respawn(0x12345678).Write(buffer);
        bool isRead = Respawn.TryRead(RespawnBytes, out Respawn message);

        Assert.That(written, Is.EqualTo(6));
        Assert.That(buffer, Is.EqualTo(RespawnBytes));
        Assert.That(isRead, Is.True);
        Assert.That(message.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void Respawn_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(RespawnBytes, ReadRespawn);
        WireMatrix.AssertRejectsTrailingData(RespawnBytes, ReadRespawn);
        WireMatrix.AssertRejectsOtherOpcodes(RespawnBytes, ReadRespawn);
    }
}
}
