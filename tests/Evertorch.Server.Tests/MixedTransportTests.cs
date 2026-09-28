using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Milestone 8's exit criterion on the wire (ROADMAP §8): a desktop client over UDP and a browser's client over
///     WebSocket, each signed in at the gateway with its own login and password, play in one map instance and see each
///     other. Both clients are the client's production networking and gameplay code; the browser's runs over the
///     managed WebSocket, which speaks the same framing as the page's bridge.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MixedTransportTests
{
    private const string Password = "Correct-Horse-9";
    private const float WalkedDistance = 2f;

    private static readonly TimeSpan ReadyLimit = TimeSpan.FromSeconds(10);

    private static IHost StartHost(TemporaryDirectory root, TestCertificate certificate)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        IHost host = TestHosts.CreateBuilderWithGateway(
                new[] { "--Network:Port=0" },
                root.Path,
                new InMemoryGameStore(),
                certificate)
            .Build();
        host.Start();
        HealthProbe health = host.Services.GetRequiredService<HealthProbe>();
        var elapsed = Stopwatch.StartNew();
        while (!health.Evaluate().IsReady && elapsed.Elapsed < ReadyLimit)
        {
            Thread.Sleep(20);
        }

        Assert.That(health.Evaluate().IsReady, Is.True, "ready");
        return host;
    }

    private static void CreateAccount(IHost host, string login)
    {
        AccountCommandResult result = host.Services.GetRequiredService<IAdminCommandService>()
            .CreateAccountAsync(AdminActor.LocalConsole, login, Password)
            .GetAwaiter()
            .GetResult();
        Assert.That(result.Outcome, Is.EqualTo(AccountCommandOutcome.Created));
    }

    private static WorldPosition? Draws(SocketClient viewer, EntityId entity)
    {
        ClientWorld world = viewer.World;
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
            && remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition position, out WorldDirection _)
                ? position
                : null;
    }

    private static float Horizontal(WorldPosition a, WorldPosition b)
    {
        float deltaX = a.X - b.X;
        float deltaZ = a.Z - b.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static bool IsInWorld(SocketClient client)
    {
        return client.Connection.World?.Inventory.IsCurrent == true;
    }

    private static void WalkWhileWatched(SocketClient walker, SocketClient watcher, float directionX, string step)
    {
        EntityId walking = walker.World.LocalEntity;
        WorldPosition before = Draws(watcher, walking)!.Value;
        walker.Controller.SetManualDirection(directionX, 0f);
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), walker, watcher);
        walker.Controller.SetManualDirection(0f, 0f);
        bool hasMoved = SocketClients.PumpUntil(
            () => Horizontal(Draws(watcher, walking) ?? before, before) > WalkedDistance,
            walker,
            watcher);
        Assert.That(hasMoved, Is.True, step);
    }

    [Test]
    public void DesktopOverUdp_AndBrowserOverWebSocket_PlayInOneMap_AndSeeEachOther()
    {
        using var certificate = new TestCertificate();
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(root, certificate);
        CreateAccount(host, "desktop");
        CreateAccount(host, "browser");
        int gatewayPort = host.Services.GetRequiredService<GatewayEndpoint>().Port;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        GatewaySignInResult desktopSignIn = SocketClient.SignIn(certificate, gatewayPort, "desktop", Password);
        GatewaySignInResult browserSignIn = SocketClient.SignIn(
            certificate,
            gatewayPort,
            "browser",
            Password,
            SignInAnswer.WebSocketTransport);
        using var desktop = SocketClient.WithToken(content, desktopSignIn.Token, "DeskWalker");
        using var browser = SocketClient.OverWebSocket(content, browserSignIn.Token, "WebWalker", certificate);

        desktop.Connect(desktopSignIn.Port);
        browser.Connect(browserSignIn.Port);

        Assert.That(
            SocketClients.PumpUntil(() => IsInWorld(desktop) && IsInWorld(browser), desktop, browser),
            Is.True,
            $"both entered the world: {desktop.Connection.LocalError} {browser.Connection.LocalError}");
        Assert.That(browserSignIn.Port, Is.EqualTo(gatewayPort), "the browser plays through the gateway");
        Assert.That(desktopSignIn.Port, Is.Not.EqualTo(gatewayPort), "the desktop plays over UDP");
        Assert.That(browser.World.Map, Is.EqualTo(desktop.World.Map), "one map");
        bool isSeen = SocketClients.PumpUntil(
            () => Draws(desktop, browser.World.LocalEntity) != null
                && Draws(browser, desktop.World.LocalEntity) != null,
            desktop,
            browser);
        Assert.That(isSeen, Is.True, "each sees the other");
        Assert.That(desktop.World.Remotes[browser.World.LocalEntity].Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(browser.World.Remotes[desktop.World.LocalEntity].Kind, Is.EqualTo(EntityKind.Player));

        WalkWhileWatched(browser, desktop, 1f, "the desktop sees the browser walk");
        WalkWhileWatched(desktop, browser, -1f, "the browser sees the desktop walk");

        host.StopAsync().GetAwaiter().GetResult();

        foreach (SocketClient client in new[] { desktop, browser })
        {
            Assert.That(
                client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected),
                Is.True,
                "the stop reached both");
            Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance));
        }
    }
}
}
