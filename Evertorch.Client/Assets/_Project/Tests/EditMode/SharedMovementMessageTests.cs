using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedMovementMessageTests
{
    private static readonly byte[] MoveInputBytes =
    {
        0x03, 0x00,
        0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A,
        0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x00, 0xBF
    };

    private static readonly byte[] StopMovementBytes =
    {
        0x04, 0x00,
        0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly byte[] EntitySnapshotBytes =
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
        0x00, 0x00
    };

    [Test]
    public void EntitySnapshot_WhenCountDisagreesWithLength_IsRejected()
    {
        byte[] invalid = (byte[])EntitySnapshotBytes.Clone();
        invalid[10] = 3;

        Assert.That(EntitySnapshot.TryRead(invalid, out EntitySnapshot? _), Is.False);
    }

    [Test]
    public void EntitySnapshot_WriteAndRead_MatchGoldenBytes()
    {
        var moving = new EntityState(
            new EntityId(0x0123456789ABCDEF),
            new WorldPosition(1f, 0.5f, -2f),
            new WorldDirection(0f, 1f),
            5f,
            0f,
            0f,
            EntityStateFlags.Moving);
        var idle = new EntityState(
            new EntityId(2),
            new WorldPosition(0f, 0f, 0f),
            new WorldDirection(1f, 0f),
            0f,
            0f,
            0f,
            EntityStateFlags.None);
        var snapshot = new EntitySnapshot(0x00010203, 0x01020304, new[] { moving, idle });
        byte[] buffer = new byte[snapshot.GetEncodedLength()];
        snapshot.Write(buffer);

        bool isRead = EntitySnapshot.TryRead(EntitySnapshotBytes, out EntitySnapshot? read);

        Assert.That(buffer, Is.EqualTo(EntitySnapshotBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.ServerTick, Is.EqualTo(0x00010203u));
        Assert.That(read.LastProcessedInputSequence, Is.EqualTo(0x01020304u));
        Assert.That(read.Entities.Count, Is.EqualTo(2));
        Assert.That(read.Entities[0].Position, Is.EqualTo(new WorldPosition(1f, 0.5f, -2f)));
        Assert.That(read.Entities[0].VelocityX, Is.EqualTo(5f));
        Assert.That(read.Entities[0].StateFlags, Is.EqualTo(EntityStateFlags.Moving));
        Assert.That(read.Entities[1].Entity, Is.EqualTo(new EntityId(2)));
    }

    [Test]
    public void MessageRouting_ForRealtimeMessages_UsesSeparateUnreliableChannels()
    {
        MessageRouting.TryGetRoute(MessageOpcode.MoveInput, out ProtocolChannel input,
            out MessageDelivery inputDelivery);
        MessageRouting.TryGetRoute(MessageOpcode.EntitySnapshot, out ProtocolChannel state,
            out MessageDelivery stateDelivery);

        Assert.That(input, Is.EqualTo(ProtocolChannel.Input));
        Assert.That(state, Is.EqualTo(ProtocolChannel.State));
        Assert.That(inputDelivery, Is.EqualTo(MessageDelivery.UnreliableSequenced));
        Assert.That(stateDelivery, Is.EqualTo(MessageDelivery.UnreliableSequenced));
    }

    [Test]
    public void MoveInput_WithNotANumberDirection_IsRejected()
    {
        byte[] invalid = (byte[])MoveInputBytes.Clone();
        invalid[12] = 0xC0;
        invalid[13] = 0x7F;

        Assert.That(MoveInput.TryRead(invalid, out MoveInput _), Is.False);
    }

    [Test]
    public void MoveInput_WriteAndRead_MatchGoldenBytes()
    {
        var intent = new MoveIntent(0x01020304, 0x0A0B0C0D, 1f, -0.5f);
        byte[] buffer = new byte[MoveInput.EncodedLength];
        new MoveInput(intent).Write(buffer);

        bool isRead = MoveInput.TryRead(MoveInputBytes, out MoveInput read);

        Assert.That(buffer, Is.EqualTo(MoveInputBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Intent, Is.EqualTo(intent));
    }

    [Test]
    public void StopMovement_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[StopMovement.EncodedLength];
        new StopMovement(0x01020304, 0x0A0B0C0D).Write(buffer);

        bool isRead = StopMovement.TryRead(StopMovementBytes, out StopMovement read);

        Assert.That(buffer, Is.EqualTo(StopMovementBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Sequence, Is.EqualTo(0x01020304u));
        Assert.That(read.ClientTick, Is.EqualTo(0x0A0B0C0Du));
    }
}
}
