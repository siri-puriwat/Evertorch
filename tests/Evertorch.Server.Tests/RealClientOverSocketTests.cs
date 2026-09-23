using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
///     The whole thing for real: the composed server host with its tick thread and UDP socket, and the client's own
///     transport, connection, prediction, and link simulation, talking over loopback in real time.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RealClientOverSocketTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private static bool PumpUntil(ClientConnection connection, Ticker? ticker, Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Limit)
        {
            connection.Poll();
            ticker?.Run();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    private static float Distance(WorldPosition left, WorldPosition right)
    {
        float deltaX = left.X - right.X;
        float deltaZ = left.Z - right.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    /// <summary>
    ///     What the Unity frame loop does for the client: whole ticks at the server's rate, from a real clock.
    /// </summary>
    private sealed class Ticker
    {
        private readonly LocalPlayerDriver m_driver;
        private readonly ClientWorld m_world;
        private readonly Stopwatch m_clock;
        private readonly FixedTickClock m_ticks;
        private double m_last;

        public Ticker(LocalPlayerDriver driver, ClientWorld world, Stopwatch clock, double tickSeconds)
        {
            m_driver = driver;
            m_world = world;
            m_clock = clock;
            m_ticks = new FixedTickClock((float)tickSeconds);
            m_last = clock.Elapsed.TotalSeconds;
        }

        public void Run()
        {
            double now = m_clock.Elapsed.TotalSeconds;
            float delta = (float)(now - m_last);
            m_last = now;
            int due = m_ticks.Advance(delta);
            for (int index = 0; index < due; index++)
            {
                m_driver.Tick(m_ticks.NextTick());
            }

            m_world.Advance(delta);
        }
    }

    private sealed class ContentMaps : IMapProvider
    {
        private readonly ServerContent m_content;

        public ContentMaps(ServerContent content)
        {
            m_content = content;
        }

        public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
        {
            bool found = m_content.Maps.TryGetValue(map, out MapDefinition? definition);
            grid = definition?.Navigation;
            return found;
        }
    }

    [Test]
    public void Client_WalksOverALossyLink_AndEndsWhereTheServerReportsIt()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        using IHost host = TestHosts
            .CreateBuilder(new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true" }, root.Path)
            .Build();
        host.Start();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();

        var clock = Stopwatch.StartNew();
        using var socket = new LiteNetLibClientTransport("evertorch", 5000);
        var link = new LossyTransport(socket, 4, () => clock.Elapsed.TotalSeconds)
        {
            LatencyMilliseconds = 50,
            JitterMilliseconds = 10,
            LossPercent = 10,
            ReorderPercent = 5
        };
        var connection = new ClientConnection(
            link,
            new ClientConnectionSettings(
                CompatibilityOptions.DefaultBuildVersion,
                content.ClientContentVersion,
                "dev:socket-test",
                new CharacterId(21)),
            new ContentMaps(content));

        connection.Connect("127.0.0.1", port);
        Assert.That(
            PumpUntil(connection, null, () => connection.State == ClientConnectionState.InWorld),
            Is.True,
            $"entered the world: {connection.LocalError} {connection.DisconnectCause}");
        ClientWorld world = connection.World!;
        var controller = new MovementController(world.Grid);
        var driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), world, connection);
        var ticker = new Ticker(driver, world, clock, 1.0 / connection.ServerTickRate);

        controller.SetManualDirection(1f, 0.5f);
        TimeSpan walkUntil = clock.Elapsed + TimeSpan.FromSeconds(1.5);
        PumpUntil(connection, ticker, () => clock.Elapsed >= walkUntil);
        controller.SetManualDirection(0f, 0f);

        // The admin view is republished once a second, so agreement shows up within two of those.
        bool agreed = PumpUntil(
            connection,
            ticker,
            () => world.Predictor.PendingCount == 0
                && admin.GetPlayers().Any(player => Distance(player.Position, world.Predictor.Position) <= 1e-3f));

        WorldPosition predicted = world.Predictor.Position;
        string serverView = string.Join(", ", admin.GetPlayers().Select(player => player.Position.ToString()));
        Assert.That(agreed, Is.True, $"client at {predicted}, server reports {serverView}");
        Assert.That(predicted.X, Is.GreaterThan(3f), "the player really walked");
        Assert.That(link.Dropped, Is.GreaterThan(0), "the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.EqualTo(0));
        Assert.That(connection.MalformedMessages, Is.EqualTo(0));

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            PumpUntil(connection, null, () => connection.State == ClientConnectionState.Disconnected),
            Is.True);
        Assert.That(connection.Notice, Is.Not.Null, "the shutdown notice reached the client's own transport");
        Assert.That(connection.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
    }
}
}
