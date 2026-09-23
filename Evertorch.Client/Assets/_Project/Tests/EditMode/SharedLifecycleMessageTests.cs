using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedLifecycleMessageTests
{
    private static readonly byte[] ClientHelloBytes =
    {
        0x01, 0x00,
        0x01, 0x00,
        0x05, 0x00, 0x30, 0x2E, 0x32, 0x2E, 0x30,
        0xD1, 0x6B, 0x32, 0x11,
        0x07, 0x00, 0x64, 0x65, 0x76, 0x3A, 0x61, 0x6E, 0x6E
    };

    private static readonly byte[] EnterWorldRequestBytes =
    {
        0x02, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01
    };

    private static readonly byte[] ServerHelloBytes =
    {
        0x01, 0x80,
        0x01, 0x00,
        0x05, 0x00, 0x30, 0x2E, 0x32, 0x2E, 0x30,
        0xD1, 0x6B, 0x32, 0x11,
        0x14, 0x00, 0x00, 0x00,
        0xE6, 0xD5, 0xC4, 0xB3, 0xA2, 0x01, 0x00, 0x00
    };

    private static readonly byte[] WorldEnteredBytes =
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
        0x00, 0x00, 0xC0, 0x3F,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static readonly byte[] EntitySpawnBytes =
    {
        0x04, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x01,
        0x05, 0x00, 0x6A, 0x6F, 0x62, 0x2E, 0x61,
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x01, 0x00,
        0x00, 0x00
    };

    private static readonly byte[] MonsterSpawnBytes =
    {
        0x04, 0x80,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02,
        0x09, 0x00, 0x6D, 0x6F, 0x6E, 0x73, 0x74, 0x65, 0x72, 0x2E, 0x61,
        0x00, 0x00, 0x40, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x41,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00,
        0xE8, 0x03
    };

    private static readonly byte[] EntityDespawnBytes =
    {
        0x05, 0x80,
        0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        0x02
    };

    private static readonly byte[] DisconnectNoticeBytes =
    {
        0x13, 0x80,
        0x03,
        0x06, 0x00, 0x55, 0x70, 0x64, 0x61, 0x74, 0x65
    };

    private static byte[] WithKind(byte[] golden, byte kind)
    {
        byte[] copy = (byte[])golden.Clone();
        copy[10] = kind;
        return copy;
    }

    [Test]
    public void ClientHello_WithInvalidUtf8_IsRejected()
    {
        byte[] invalid = (byte[])ClientHelloBytes.Clone();
        invalid[17] = 0xC3;
        invalid[18] = 0x28;

        Assert.That(ClientHello.TryRead(invalid, out ClientHello? _), Is.False);
    }

    [Test]
    public void ClientHello_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ClientHello(1, "0.2.0", 0x11326BD1, "dev:ann");
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ClientHello.TryRead(ClientHelloBytes, out ClientHello? read);

        Assert.That(buffer, Is.EqualTo(ClientHelloBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.ClientBuildVersion, Is.EqualTo("0.2.0"));
        Assert.That(read.ClientContentVersion, Is.EqualTo(0x11326BD1u));
        Assert.That(read.SessionToken, Is.EqualTo("dev:ann"));
    }

    [Test]
    public void ContentVersionCodec_ForManifestVersion_UsesTheFirstEightDigits()
    {
        bool isConverted = ContentVersionCodec.TryToWire("11326bd1bdfe0c49", out uint wire);

        Assert.That(isConverted, Is.True);
        Assert.That(wire, Is.EqualTo(0x11326BD1u));
        Assert.That(ContentVersionCodec.TryToWire("11326BD1BDFE0C49", out uint _), Is.False);
    }

    [Test]
    public void DisconnectNotice_WriteAndRead_MatchGoldenBytes()
    {
        var message = new DisconnectNotice(DisconnectReason.ContentUpdateRequired, "Update");
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = DisconnectNotice.TryRead(DisconnectNoticeBytes, out DisconnectNotice? read);

        Assert.That(buffer, Is.EqualTo(DisconnectNoticeBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Reason, Is.EqualTo(DisconnectReason.ContentUpdateRequired));
        Assert.That(read.Message, Is.EqualTo("Update"));
    }

    [Test]
    public void EnterWorldRequest_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(0x0102030405060708)).Write(buffer);

        bool isRead = EnterWorldRequest.TryRead(EnterWorldRequestBytes, out EnterWorldRequest read);

        Assert.That(buffer, Is.EqualTo(EnterWorldRequestBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Character, Is.EqualTo(new CharacterId(0x0102030405060708)));
    }

    [Test]
    public void EntityDespawn_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(0x0123456789ABCDEF), DespawnReason.Removed).Write(buffer);

        bool isRead = EntityDespawn.TryRead(EntityDespawnBytes, out EntityDespawn read);

        Assert.That(buffer, Is.EqualTo(EntityDespawnBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Reason, Is.EqualTo(DespawnReason.Removed));
    }

    [Test]
    public void EntitySpawn_WriteAndRead_MatchGoldenBytes()
    {
        var message = new EntitySpawn(
            new EntityId(0x0123456789ABCDEF),
            EntityKind.Player,
            "job.a",
            new WorldPosition(1f, 0.5f, -2f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.Moving,
            0);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = EntitySpawn.TryRead(EntitySpawnBytes, out EntitySpawn? read);

        Assert.That(buffer, Is.EqualTo(EntitySpawnBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.DefinitionId, Is.EqualTo("job.a"));
        Assert.That(read.StateFlags, Is.EqualTo(EntityStateFlags.Moving));
    }

    [Test]
    public void MessageRouting_ForLifecycleMessages_UsesTheControlChannel()
    {
        bool hasRoute = MessageRouting.TryGetRoute(
            MessageOpcode.EntitySpawn,
            out ProtocolChannel channel,
            out MessageDelivery delivery);

        Assert.That(hasRoute, Is.True);
        Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
    }

    [Test]
    public void MonsterSpawn_WriteAndRead_MatchGoldenBytes()
    {
        var message = new EntitySpawn(
            new EntityId(42),
            EntityKind.Monster,
            "monster.a",
            new WorldPosition(12f, 0f, 12f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            1000);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = EntitySpawn.TryRead(MonsterSpawnBytes, out EntitySpawn? read);
        bool isJobAccepted = EntitySpawn.TryRead(WithKind(EntitySpawnBytes, 0x02), out EntitySpawn? _);

        Assert.That(buffer, Is.EqualTo(MonsterSpawnBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Kind, Is.EqualTo(EntityKind.Monster));
        Assert.That(isJobAccepted, Is.False);
    }

    [Test]
    public void ServerHello_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ServerHello(1, "0.2.0", 0x11326BD1, 20, 0x000001A2B3C4D5E6);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ServerHello.TryRead(ServerHelloBytes, out ServerHello? read);

        Assert.That(buffer, Is.EqualTo(ServerHelloBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.ServerTickRate, Is.EqualTo(20u));
        Assert.That(read.ServerTimeUnixMilliseconds, Is.EqualTo(0x000001A2B3C4D5E6));
    }

    [Test]
    public void WorldEntered_WithNotANumber_IsRejected()
    {
        byte[] invalid = (byte[])WorldEnteredBytes.Clone();
        invalid[34] = 0xC0;
        invalid[35] = 0x7F;

        Assert.That(WorldEntered.TryRead(invalid, out WorldEntered? _), Is.False);
    }

    [Test]
    public void WorldEntered_WriteAndRead_MatchGoldenBytes()
    {
        var message = new WorldEntered(
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
            1.5f,
            0x0A0B0C0D);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = WorldEntered.TryRead(WorldEnteredBytes, out WorldEntered? read);

        Assert.That(buffer, Is.EqualTo(WorldEnteredBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Map, Is.EqualTo(new MapDefinitionId("map.a")));
        Assert.That(read.Job, Is.EqualTo(new JobDefinitionId("job.a")));
        Assert.That(read.Position, Is.EqualTo(new WorldPosition(1f, 0.5f, -2f)));
        Assert.That(read.MovementSpeed, Is.EqualTo(5f));
        Assert.That(read.LastCommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }
}
}
