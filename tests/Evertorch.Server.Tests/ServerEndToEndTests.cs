using System.IO;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using DisconnectReason = Evertorch.Protocol.DisconnectReason;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The composed host — real configuration, content, tick thread, and socket — driven by a bare UDP client.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ServerEndToEndTests
{
    private static IHost StartHost(TemporaryDirectory root, bool enableDevelopmentAuthentication)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        string[] args = enableDevelopmentAuthentication
            ? new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true" }
            : new[] { "--Network:Port=0" };
        IHost host = ServerHost.CreateBuilder(args, root.Path).Build();
        host.Start();
        return host;
    }

    private static void EnterWorld(TestNetClient client, int port, uint contentVersion, long character)
    {
        client.Connect(port, "evertorch");
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True, "the client did not connect");
        client.Send(Hello(contentVersion), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        Assert.That(client.WaitFor(() => Control(client).Length >= 1), Is.True, "no ServerHello arrived");

        byte[] request = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(character)).Write(request);
        client.Send(request, ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        Assert.That(client.WaitFor(() => Control(client).Length >= 2), Is.True, "no WorldEntered arrived");
    }

    private static byte[] Hello(uint contentVersion)
    {
        var hello = new ClientHello(
            ProtocolConstants.ProtocolVersion,
            CompatibilityOptions.DefaultBuildVersion,
            contentVersion,
            "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static TestNetClient.ReceivedMessage[] Control(TestNetClient client)
    {
        return client.Received.Where(message => message.Channel == (byte)ProtocolChannel.Control).ToArray();
    }

    private static EntitySnapshot[] Snapshots(TestNetClient client)
    {
        return client.Received
            .Where(message => message.Channel == (byte)ProtocolChannel.State)
            .Select(message =>
            {
                EntitySnapshot.TryRead(message.Payload, out EntitySnapshot? snapshot);
                return snapshot!;
            })
            .ToArray();
    }

    private static MessageOpcode[] Opcodes(TestNetClient.ReceivedMessage[] messages)
    {
        return messages
            .Select(message =>
            {
                MessageRouting.TryReadOpcode(message.Payload, out MessageOpcode opcode);
                return opcode;
            })
            .ToArray();
    }

    private static EntitySpawn[] Spawns(TestNetClient client, EntityKind kind)
    {
        return Control(client)
            .Select(message => EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn != null && spawn.Kind == kind)
            .Select(spawn => spawn!)
            .ToArray();
    }

    private static bool Mentions(EntitySnapshot snapshot, EntityId entity)
    {
        return snapshot.Entities.Any(state => state.Entity == entity);
    }

    [Test]
    public void Client_OverLoopbackSocket_CompletesHandshakeEntersWorldAndSeesAnotherPlayer()
    {
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(root, true);
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        uint contentVersion = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;
        using var first = new TestNetClient();
        using var second = new TestNetClient();

        EnterWorld(first, port, contentVersion, 1);
        EnterWorld(second, port, contentVersion, 2);

        Assert.That(first.WaitFor(() => Spawns(first, EntityKind.Player).Length == 1), Is.True,
            "the first client never saw the second");
        Assert.That(second.WaitFor(() => Spawns(second, EntityKind.Player).Length == 1), Is.True,
            "the second client never saw the first");
        Assert.That(
            Spawns(second, EntityKind.Monster).Select(spawn => spawn.DefinitionId),
            Is.EqualTo(Enumerable.Repeat("monster.training_slime", 4)),
            "the training ground's four slimes are in view of its spawn point");
        Assert.That(Opcodes(Control(second)).Take(3), Is.EqualTo(new[]
        {
            MessageOpcode.ServerHello,
            MessageOpcode.WorldEntered,
            MessageOpcode.EntitySpawn
        }));
        Assert.That(Opcodes(Control(first)).Last(), Is.EqualTo(MessageOpcode.EntitySpawn));
        WorldEntered.TryRead(Control(second)[1].Payload, out WorldEntered? entered);
        Assert.That(entered!.Map, Is.EqualTo(new MapDefinitionId("map.training_ground")));
        Assert.That(entered.Job, Is.EqualTo(new JobDefinitionId("job.adventurer")));
        Assert.That(entered.MovementSpeed, Is.EqualTo(5f));

        EntityId firstEntity = Spawns(second, EntityKind.Player)[0].Entity;
        Assert.That(second.WaitFor(() => Snapshots(second).Any(snapshot => Mentions(snapshot, firstEntity))), Is.True);
        EntitySnapshot both = Snapshots(second).Last(snapshot => Mentions(snapshot, firstEntity));
        Assert.That(
            both.Entities[0].Entity,
            Is.EqualTo(entered.LocalEntity),
            "a client's own entity leads its snapshot");
        Assert.That(
            second.Received.Where(message => message.Channel == (byte)ProtocolChannel.State).Select(m => m.Method),
            Is.All.EqualTo(DeliveryMethod.Sequenced));

        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(first.WaitFor(() => first.IsDisconnected), Is.True);
        Assert.That(first.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
    }

    [Test]
    public void Client_WhenDevelopmentAuthenticationIsOffByDefault_IsRefused()
    {
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(root, false);
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        uint contentVersion = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;
        using var client = new TestNetClient();
        client.Connect(port, "evertorch");
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True);

        client.Send(Hello(contentVersion), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(DisconnectReason.AuthenticationFailed));
        Assert.That(client.Received, Is.Empty);
        host.StopAsync().GetAwaiter().GetResult();
    }
}
}
