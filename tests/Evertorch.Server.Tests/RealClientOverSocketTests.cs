using System;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The whole thing for real: the composed server host with its tick thread and UDP socket, and the client's own
///     transport, connection, prediction, and link simulation, talking over loopback in real time.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RealClientOverSocketTests
{
    [Test]
    public void Client_ReconnectingOverTheSocket_GetsTheSameEntityAndAFreshBaselineWithoutASecondLoad()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        var store = new InMemoryGameStore();
        using IHost host = TestHosts
            .CreateBuilder(new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true" }, root.Path, store)
            .Build();
        host.Start();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();

        using var first = new SocketClient(content, "socket-reconnect", "Socket23");
        first.EnterWorld(port);
        EntityId entity = first.World.LocalEntity;
        long character = first.Connection.Characters.Single().Character.Value;
        first.Disconnect();

        using var second = new SocketClient(content, "socket-reconnect", "Socket23");
        second.EnterWorld(port);

        Assert.That(second.World.LocalEntity, Is.EqualTo(entity), "attached to the retained entity");
        Assert.That(second.World.Inventory.IsCurrent, Is.True, "the baseline ended with the inventory");
        Assert.That(store.Loads[character], Is.EqualTo(1), "no second copy was loaded");
        Assert.That(second.Connection.MalformedMessages + second.Connection.UnexpectedMessages, Is.Zero);
        Assert.That(
            host.Services.GetRequiredService<SessionRegistry>().Characters,
            Has.Count.EqualTo(1));
        host.StopAsync().GetAwaiter().GetResult();
    }

    [Test]
    public void Client_WalksOverALossyLink_AndEndsWhereTheServerReportsIt()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        var store = new InMemoryGameStore();
        using IHost host = TestHosts
            .CreateBuilder(new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true" }, root.Path, store)
            .Build();
        host.Start();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();

        using var client = new SocketClient(content, "socket-test", "Socket21");
        LossyTransport link = client.Link;
        link.LatencyMilliseconds = 50;
        link.JitterMilliseconds = 10;
        link.LossPercent = 10;
        link.ReorderPercent = 5;
        client.Connect(port);
        Assert.That(
            client.PumpUntil(() => client.Connection.State == ClientConnectionState.InWorld),
            Is.True,
            $"entered the world: {client.Connection.LocalError} {client.Connection.DisconnectCause}");
        ClientWorld world = client.World;

        client.Controller.SetManualDirection(1f, 0.5f);
        client.PumpFor(TimeSpan.FromSeconds(1.5));
        client.Controller.SetManualDirection(0f, 0f);

        // The admin view is republished once a second, so agreement shows up within two of those.
        bool agreed = client.PumpUntil(() =>
            world.Predictor.PendingCount == 0
            && admin.GetPlayers(AdminActor.LocalConsole).Any(player => client.DistanceTo(player.Position) <= 1e-3f));

        WorldPosition predicted = world.Predictor.Position;
        string serverView = string.Join(
            ", ",
            admin.GetPlayers(AdminActor.LocalConsole).Select(player => player.Position.ToString()));
        Assert.That(agreed, Is.True, $"client at {predicted}, server reports {serverView}");
        Assert.That(predicted.X, Is.GreaterThan(3f), "the player really walked");
        Assert.That(link.Dropped, Is.GreaterThan(0), "the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.EqualTo(0));
        Assert.That(client.Connection.MalformedMessages, Is.EqualTo(0));

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected),
            Is.True);
        Assert.That(client.Connection.Notice, Is.Not.Null, "the shutdown notice reached the client's own transport");
        Assert.That(
            store.Checkpoints,
            Has.Some.Matches<CharacterCheckpoint>(checkpoint => checkpoint.Position.X > 3f),
            "the controlled shutdown checkpointed the character where it walked to");
        Assert.That(client.Connection.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
    }
}
}
