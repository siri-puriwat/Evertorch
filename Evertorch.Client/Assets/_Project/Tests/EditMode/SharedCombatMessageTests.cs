using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the combat messages so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedCombatMessageTests
{
    private static readonly byte[] AttackBytes =
    {
        0x06, 0x00,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x78, 0x56, 0x34, 0x12
    };

    private static readonly byte[] CancelBytes = { 0x07, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static readonly byte[] RespawnBytes = { 0x0C, 0x00, 0x78, 0x56, 0x34, 0x12 };

    private static readonly byte[] CommandRejectedBytes = { 0x18, 0x80, 0x78, 0x56, 0x34, 0x12, 0x07 };

    private static readonly byte[] ItemDroppedBytes =
    {
        0x0D, 0x80,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x17, 0x00,
        0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6D, 0x61, 0x74, 0x65, 0x72, 0x69, 0x61,
        0x6C, 0x2E, 0x73, 0x6C, 0x69, 0x6D, 0x65, 0x5F, 0x67, 0x65, 0x6C,
        0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x48, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x38, 0x41
    };

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

    [Test]
    public void AttackEntity_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[AttackEntity.EncodedLength];
        new AttackEntity(new EntityId(0x0123456789ABCDEF), 0x12345678).Write(buffer);

        bool isRead = AttackEntity.TryRead(AttackBytes, out AttackEntity read);

        Assert.That(buffer, Is.EqualTo(AttackBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void CancelAndRespawn_WriteAndRead_MatchGoldenBytes()
    {
        byte[] cancel = new byte[CancelAction.EncodedLength];
        byte[] respawn = new byte[Respawn.EncodedLength];
        new CancelAction(0x12345678).Write(cancel);
        new Respawn(0x12345678).Write(respawn);

        Assert.That(cancel, Is.EqualTo(CancelBytes));
        Assert.That(respawn, Is.EqualTo(RespawnBytes));
        Assert.That(CancelAction.TryRead(CancelBytes, out CancelAction _), Is.True);
        Assert.That(Respawn.TryRead(RespawnBytes, out Respawn _), Is.True);
    }

    [Test]
    public void CommandRejected_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[CommandRejected.EncodedLength];
        new CommandRejected(0x12345678, CommandRejectionReason.Busy).Write(buffer);

        bool isRead = CommandRejected.TryRead(CommandRejectedBytes, out CommandRejected read);

        Assert.That(buffer, Is.EqualTo(CommandRejectedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
        Assert.That(read.Reason, Is.EqualTo(CommandRejectionReason.Busy));
    }

    [Test]
    public void ItemDropped_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ItemDropped(new EntityId(7), "item.material.slime_gel", 2,
            new WorldPosition(12.5f, 0f, 11.5f));
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        bool isRead = ItemDropped.TryRead(ItemDroppedBytes, out ItemDropped? read);

        Assert.That(written, Is.EqualTo(ItemDroppedBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Amount, Is.EqualTo(2u));
        Assert.That(ProtocolConstants.ProtocolVersion, Is.EqualTo(25));
    }

    [Test]
    public void ServerCombatEvents_WriteAndRead_MatchGoldenBytes()
    {
        var timing = new AttackTiming(
            TimeSpan.FromMilliseconds(940),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(235));

        byte[] writtenStart = new byte[AttackStarted.EncodedLength];
        new AttackStarted(new EntityId(1), new EntityId(42), 16, timing).Write(writtenStart);
        byte[] writtenDamage = new byte[Damage.EncodedLength];
        new Damage(new EntityId(1), new EntityId(42), CombatResult.Critical, 68, 32, 40).Write(writtenDamage);
        byte[] writtenDied = new byte[EntityDied.EncodedLength];
        new EntityDied(new EntityId(42), new EntityId(1), 32).Write(writtenDied);
        byte[] writtenHealth = new byte[CharacterHealth.EncodedLength];
        new CharacterHealth(60, 68, 20, 24).Write(writtenHealth);
        byte[] writtenRevived = new byte[EntityRevived.EncodedLength];
        new EntityRevived(new EntityId(1), new WorldPosition(1f, 0.5f, -2f), new WorldDirection(0f, 1f), 48)
            .Write(writtenRevived);

        Assert.That(writtenStart, Is.EqualTo(AttackStartedBytes));
        Assert.That(writtenDamage, Is.EqualTo(DamageBytes));
        Assert.That(writtenDied, Is.EqualTo(DiedBytes));
        Assert.That(writtenHealth, Is.EqualTo(HealthBytes));
        Assert.That(writtenRevived, Is.EqualTo(RevivedBytes));
        Assert.That(AttackStarted.TryRead(AttackStartedBytes, out AttackStarted readStart), Is.True);
        Assert.That(readStart.Timing, Is.EqualTo(timing));
        Assert.That(Damage.TryRead(DamageBytes, out Damage _), Is.True);
        Assert.That(EntityDied.TryRead(DiedBytes, out EntityDied _), Is.True);
        Assert.That(CharacterHealth.TryRead(HealthBytes, out CharacterHealth _), Is.True);
        Assert.That(EntityRevived.TryRead(RevivedBytes, out EntityRevived _), Is.True);
    }
}
}
