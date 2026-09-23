using System;
using System.Collections.Generic;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Hostile input must never throw, and anything a reader accepts must re-encode to the bytes it came from, so no
///     two different payloads can mean the same message.
/// </summary>
[TestFixture]
public sealed class MessageFuzzTests
{
    private const int Seed = 20260920;
    private const int RandomPayloads = 20000;
    private const int MutationsPerMessage = 5000;

    public delegate byte[]? Reencode(byte[] payload);

    private static IEnumerable<TestCaseData> Messages()
    {
        yield return Case(
            "ClientHello",
            Encode(new ClientHello(1, "0.2.0", 7, "dev:ann")),
            payload => ClientHello.TryRead(payload, out ClientHello? message) ? Encode(message!) : null);
        yield return Case(
            "EnterWorldRequest",
            Encode(new EnterWorldRequest(new CharacterId(42))),
            payload => EnterWorldRequest.TryRead(payload, out EnterWorldRequest message) ? Encode(message) : null);
        yield return Case(
            "TargetEntity",
            Encode(new TargetEntity(new EntityId(42))),
            payload => TargetEntity.TryRead(payload, out TargetEntity message) ? Encode(message) : null);
        yield return Case(
            "ServerHello",
            Encode(new ServerHello(1, "0.2.0", 7, 20, 1234567890123)),
            payload => ServerHello.TryRead(payload, out ServerHello? message) ? Encode(message!) : null);
        yield return Case(
            "WorldEntered",
            Encode(
                new WorldEntered(
                    new MapDefinitionId("map.training_ground"),
                    1,
                    new EntityId(9),
                    new JobDefinitionId("job.adventurer"),
                    100,
                    new WorldPosition(1f, 2f, 3f),
                    new WorldDirection(0f, 1f),
                    5f)),
            payload => WorldEntered.TryRead(payload, out WorldEntered? message) ? Encode(message!) : null);
        yield return Case(
            "EntitySpawn",
            Encode(
                new EntitySpawn(
                    new EntityId(9),
                    EntityKind.Player,
                    "job.adventurer",
                    new WorldPosition(1f, 2f, 3f),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.Moving)),
            payload => EntitySpawn.TryRead(payload, out EntitySpawn? message) ? Encode(message!) : null);
        yield return Case(
            "EntityDespawn",
            Encode(new EntityDespawn(new EntityId(9), DespawnReason.OutOfRange)),
            payload => EntityDespawn.TryRead(payload, out EntityDespawn message) ? Encode(message) : null);
        yield return Case(
            "MoveInput",
            Encode(new MoveInput(new MoveIntent(3, 4, 0.6f, -0.8f))),
            payload => MoveInput.TryRead(payload, out MoveInput message) ? Encode(message) : null);
        yield return Case(
            "StopMovement",
            Encode(new StopMovement(3, 4)),
            payload => StopMovement.TryRead(payload, out StopMovement message) ? Encode(message) : null);
        yield return Case(
            "EntitySnapshot",
            Encode(
                new EntitySnapshot(
                    100,
                    3,
                    new[]
                    {
                        new EntityState(
                            new EntityId(9),
                            new WorldPosition(1f, 2f, 3f),
                            new WorldDirection(0f, 1f),
                            5f,
                            0f,
                            0f,
                            EntityStateFlags.Moving),
                        new EntityState(new EntityId(10), default, new WorldDirection(1f, 0f), 0f, 0f, 0f, 0)
                    })),
            payload => EntitySnapshot.TryRead(payload, out EntitySnapshot? message) ? Encode(message!) : null);
        yield return Case(
            "DisconnectNotice",
            Encode(new DisconnectNotice(DisconnectReason.Kicked, "Bye")),
            payload => DisconnectNotice.TryRead(payload, out DisconnectNotice? message) ? Encode(message!) : null);
    }

    [TestCaseSource(nameof(Messages))]
    public void TryRead_ForRandomBytes_NeverThrowsAndAcceptsOnlyCanonicalPayloads(byte[] valid, Reencode reencode)
    {
        var random = new Random(Seed);
        byte[] opcode = { valid[0], valid[1] };

        for (int iteration = 0; iteration < RandomPayloads; iteration++)
        {
            byte[] payload = new byte[random.Next(0, 128)];
            random.NextBytes(payload);

            // Half the payloads carry the right opcode, or nearly all of them would fail on the first two bytes.
            if (payload.Length >= 2 && (iteration & 1) == 0)
            {
                payload[0] = opcode[0];
                payload[1] = opcode[1];
            }

            AssertCanonicalOrRejected(payload, reencode);
        }
    }

    [TestCaseSource(nameof(Messages))]
    public void TryRead_ForMutatedValidPayload_NeverThrowsAndAcceptsOnlyCanonicalPayloads(
        byte[] valid,
        Reencode reencode)
    {
        var random = new Random(Seed);
        Assert.That(reencode(valid), Is.EqualTo(valid), "the unmutated payload must be accepted");

        for (int iteration = 0; iteration < MutationsPerMessage; iteration++)
        {
            byte[] payload = (byte[])valid.Clone();
            int changes = random.Next(1, 4);
            for (int change = 0; change < changes; change++)
            {
                payload[random.Next(2, payload.Length)] = (byte)random.Next(256);
            }

            if (random.Next(4) == 0)
            {
                Array.Resize(ref payload, random.Next(0, payload.Length + 4));
            }

            AssertCanonicalOrRejected(payload, reencode);
        }
    }

    private static void AssertCanonicalOrRejected(byte[] payload, Reencode reencode)
    {
        byte[]? reencoded = null;
        Action read = () => reencoded = reencode(payload);

        Assert.That(read, Throws.Nothing, BitConverter.ToString(payload));
        if (reencoded != null)
        {
            Assert.That(reencoded, Is.EqualTo(payload), "accepted a payload that is not its own encoding");
        }
    }

    private static TestCaseData Case(string name, byte[] valid, Reencode reencode)
    {
        return new TestCaseData(valid, reencode).SetArgDisplayNames(name);
    }

    private static byte[] Encode(ClientHello message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(ServerHello message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(WorldEntered message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntitySpawn message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(DisconnectNotice message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntitySnapshot message)
    {
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(MoveInput message)
    {
        byte[] buffer = new byte[MoveInput.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(StopMovement message)
    {
        byte[] buffer = new byte[StopMovement.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EnterWorldRequest message)
    {
        byte[] buffer = new byte[EnterWorldRequest.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(EntityDespawn message)
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];
        message.Write(buffer);
        return buffer;
    }

    private static byte[] Encode(TargetEntity message)
    {
        byte[] buffer = new byte[TargetEntity.EncodedLength];
        message.Write(buffer);
        return buffer;
    }
}
}
