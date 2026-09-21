using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
/// The composed host — real configuration, content, tick thread, and socket — driven by a bare UDP client.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ServerEndToEndTests
{
    [Test]
    public void Client_OverLoopbackSocket_CompletesHandshakeEntersWorldAndSeesAnotherPlayer()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        using IHost host = StartHost(root, true);
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        uint contentVersion = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;
        using TestNetClient first = new TestNetClient();
        using TestNetClient second = new TestNetClient();

        EnterWorld(first, port, contentVersion, 1);
        EnterWorld(second, port, contentVersion, 2);

        Assert.That(first.WaitFor(() => first.Received.Count >= 3), Is.True, "the first client never saw the second");
        Assert.That(second.WaitFor(() => second.Received.Count >= 3), Is.True, "the second client never saw the first");
        Assert.That(Opcodes(second).Take(3), Is.EqualTo(new[]
        {
            MessageOpcode.ServerHello,
            MessageOpcode.WorldEntered,
            MessageOpcode.EntitySpawn,
        }));
        Assert.That(Opcodes(first).Last(), Is.EqualTo(MessageOpcode.EntitySpawn));
        WorldEntered.TryRead(second.Received[1].Payload, out WorldEntered? entered);
        Assert.That(entered!.Map, Is.EqualTo(new MapDefinitionId("map.training_ground")));
        Assert.That(entered.MovementSpeed, Is.EqualTo(5f));

        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(first.WaitFor(() => first.IsDisconnected), Is.True);
        Assert.That(first.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.Maintenance));
    }

    [Test]
    public void Client_WhenDevelopmentAuthenticationIsOffByDefault_IsRefused()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        using IHost host = StartHost(root, false);
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        uint contentVersion = host.Services.GetRequiredService<HandshakeValidator>().RequiredClientContentVersion;
        using TestNetClient client = new TestNetClient();
        client.Connect(port, "evertorch");
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True);

        client.Send(Hello(contentVersion), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.AuthenticationFailed));
        Assert.That(client.Received, Is.Empty);
        host.StopAsync().GetAwaiter().GetResult();
    }

    private static IHost StartHost(TemporaryDirectory root, bool enableDevelopmentAuthentication)
    {
        PackageFixture.WriteTo(
            System.IO.Path.Combine(root.Path, "content", "server"),
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
        Assert.That(client.WaitFor(() => client.Received.Count >= 1), Is.True, "no ServerHello arrived");

        byte[] request = new byte[EnterWorldRequest.EncodedLength];
        new EnterWorldRequest(new CharacterId(character)).Write(request);
        client.Send(request, ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        Assert.That(client.WaitFor(() => client.Received.Count >= 2), Is.True, "no WorldEntered arrived");
    }

    private static byte[] Hello(uint contentVersion)
    {
        ClientHello hello = new ClientHello(
            ProtocolConstants.ProtocolVersion,
            CompatibilityOptions.DefaultBuildVersion,
            contentVersion,
            "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static MessageOpcode[] Opcodes(TestNetClient client)
    {
        return client.Received
            .Select(message =>
            {
                MessageRouting.TryReadOpcode(message.Payload, out MessageOpcode opcode);
                return opcode;
            })
            .ToArray();
    }
}
}
