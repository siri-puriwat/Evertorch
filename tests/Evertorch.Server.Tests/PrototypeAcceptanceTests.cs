using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 6 prototype acceptance (Prototype Content §9) with two players, end to end: the composed server
///     host on a real PostgreSQL 18, real UDP sockets on loopback, and two clients built from the client's production
///     networking and gameplay code, pumped side by side on the test's thread. Each later line of Milestone 6 adds its
///     steps here.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, and every slime drops one gel. Monster placement and AI keep the seeded server source.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class PrototypeAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Adventurer = "job.adventurer";
    private const string TrainingSlime = "monster.training_slime";
    private const float ConvergedDistance = 1e-3f;
    private const float WalkedDistance = 2f;

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(40);

    private PostgresFixture m_database = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=5" },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    /// <summary>
    ///     Both players enter the training ground, see each other, walk, fight, and one reconnects; the server then
    ///     stops. Returns where each character stood, by character name.
    /// </summary>
    private static Dictionary<string, WorldPosition> PlayTogether(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        var stopped = new Dictionary<string, WorldPosition>();

        using var first = new SocketClient(content, Players.FirstIdentity, Players.FirstName);
        using var second = new SocketClient(content, Players.SecondIdentity, Players.SecondName);
        EnterTogether(port, first, second);
        foreach (SocketClient client in new[] { first, second })
        {
            Assert.That(
                client.World.Map,
                Is.EqualTo(new MapDefinitionId(TrainingGround)),
                "enter: both players are in the training ground");
        }

        SeeEachOther("enter", first, second);
        WatchEachOtherWalk(first, second);
        WalkOnALossyLink(admin, first, second);
        FightSlimes("fight", first, second);

        EntityId firstEntity = first.World.LocalEntity;
        EntityId secondEntity = second.World.LocalEntity;
        var despawned = new List<EntityId>();
        second.World.RemoteDespawned += remote => despawned.Add(remote.Entity);
        first.Disconnect();
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(500), second);
        Assert.That(
            second.World.Remotes.ContainsKey(firstEntity),
            Is.True,
            "reconnect: the other player still sees the retained character");

        using (var again = new SocketClient(content, Players.FirstIdentity, Players.FirstName))
        {
            again.Connect(port);
            Assert.That(
                SocketClients.PumpUntil(() => again.Connection.World?.Inventory.IsCurrent == true, again, second),
                Is.True,
                $"reconnect: entered again: {again.Connection.LocalError} {again.Connection.DisconnectCause}");
            Assert.That(again.World.LocalEntity, Is.EqualTo(firstEntity), "reconnect: attached to the same entity");
            SeeEachOther("reconnect", again, second);
            Assert.That(despawned, Does.Not.Contain(firstEntity), "reconnect: the other player never lost sight");
            AwaitConvergence(admin, "reconnect", again, second);
            stopped[Players.FirstName] = PositionOf(admin, firstEntity);
            stopped[Players.SecondName] = PositionOf(admin, secondEntity);

            host.StopAsync().GetAwaiter().GetResult();
            foreach (SocketClient client in new[] { again, second })
            {
                Assert.That(
                    SocketClients.PumpUntil(
                        () => client.Connection.State == ClientConnectionState.Disconnected,
                        client),
                    Is.True,
                    "restart: both clients heard the server stop");
                Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance), "restart");
                AssertCleanTraffic(client, "together");
            }
        }

        AssertCleanTraffic(first, "together");
        return stopped;
    }

    /// <summary>
    ///     After a clean stop and a second server on the same database, both sign in again to the same characters where
    ///     they stood, and see each other.
    /// </summary>
    private static void PlayAfterTheRestart(IHost host, IReadOnlyDictionary<string, WorldPosition> stopped)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<IServerTransport>().LocalPort;
        using var first = new SocketClient(content, Players.FirstIdentity, Players.FirstName);
        using var second = new SocketClient(content, Players.SecondIdentity, Players.SecondName);
        EnterTogether(port, first, second);
        (SocketClient Client, string Name)[] characters =
        {
            (first, Players.FirstName), (second, Players.SecondName)
        };
        foreach ((SocketClient client, string name) in characters)
        {
            Assert.That(client.Connection.Characters.Single().Name, Is.EqualTo(name), "restart: the same character");
            Assert.That(
                client.DistanceTo(stopped[name]),
                Is.LessThanOrEqualTo(ConvergedDistance),
                $"restart: {name} stands where the shutdown checkpoint left it");
        }

        SeeEachOther("restart", first, second);
        AssertCleanTraffic(first, "restart");
        AssertCleanTraffic(second, "restart");
    }

    private static void EnterTogether(int port, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            client.Connect(port);
        }

        bool isEntered = SocketClients.PumpUntil(
            () => clients.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            clients);
        string states = string.Join(
            ", ",
            clients.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"enter: both players entered the world ({states})");
    }

    // Each client has a spawn for the other, of the other's job, and draws it: interest management put them in view.
    private static void SeeEachOther(string step, SocketClient first, SocketClient second)
    {
        bool isSeen = SocketClients.PumpUntil(
            () => Draws(first, second.World.LocalEntity) != null && Draws(second, first.World.LocalEntity) != null,
            first,
            second);
        Assert.That(isSeen, Is.True, $"{step}: each player sees the other");
        foreach ((SocketClient viewer, SocketClient seen) in new[] { (first, second), (second, first) })
        {
            RemoteEntity remote = viewer.World.Remotes[seen.World.LocalEntity];
            Assert.That(remote.Kind, Is.EqualTo(EntityKind.Player), step);
            Assert.That(remote.DefinitionId, Is.EqualTo(Adventurer), step);
        }
    }

    // One walks while the other watches, then the other way round: each sees the other move.
    private static void WatchEachOtherWalk(SocketClient first, SocketClient second)
    {
        WalkWhileWatched(second, first, 1f, 0f);
        WalkWhileWatched(first, second, -1f, 0f);
    }

    private static void WalkWhileWatched(SocketClient walker, SocketClient watcher, float directionX, float directionZ)
    {
        EntityId walking = walker.World.LocalEntity;
        WorldPosition before = Draws(watcher, walking)!.Value;
        walker.Controller.SetManualDirection(directionX, directionZ);
        SocketClients.PumpFor(TimeSpan.FromSeconds(1), walker, watcher);
        walker.Controller.SetManualDirection(0f, 0f);
        bool hasMoved = SocketClients.PumpUntil(
            () => Horizontal(Draws(watcher, walking) ?? before, before) > WalkedDistance,
            walker,
            watcher);
        Assert.That(hasMoved, Is.True, "walk: each player sees the other move");
    }

    // Both walk at once over links that delay, reorder, and lose messages, then stop: each client ends where the
    // server has its own character, and draws the other where the server has that one.
    private static void WalkOnALossyLink(IAdminCommandService admin, SocketClient first, SocketClient second)
    {
        foreach (SocketClient client in new[] { first, second })
        {
            client.Link.LatencyMilliseconds = 50;
            client.Link.JitterMilliseconds = 10;
            client.Link.LossPercent = 10;
            client.Link.ReorderPercent = 5;
        }

        int dropped = first.Link.Dropped + second.Link.Dropped;
        first.Controller.SetManualDirection(0f, 1f);
        second.Controller.SetManualDirection(1f, 0f);
        SocketClients.PumpFor(TimeSpan.FromSeconds(1.5), first, second);
        first.Controller.SetManualDirection(0f, 0f);
        second.Controller.SetManualDirection(0f, 0f);
        AwaitConvergence(admin, "lossy walk", first, second);
        Assert.That(
            first.Link.Dropped + second.Link.Dropped,
            Is.GreaterThan(dropped),
            "lossy walk: messages were lost");

        foreach (SocketClient client in new[] { first, second })
        {
            Assert.That(client.World.Smoother.Snaps, Is.Zero, "lossy walk: no correction was large enough to snap");
            client.Link.LatencyMilliseconds = 0;
            client.Link.JitterMilliseconds = 0;
            client.Link.LossPercent = 0;
            client.Link.ReorderPercent = 0;
        }
    }

    // Tab, then West once the server confirms the target, for both players at once: each one's slime dies.
    private static void FightSlimes(string step, params SocketClient[] clients)
    {
        var targets = new Dictionary<SocketClient, EntityId>();
        var deaths = new Dictionary<SocketClient, List<EntityId>>();
        foreach (SocketClient client in clients)
        {
            var died = new List<EntityId>();
            deaths[client] = died;
            client.World.EntityDiedReceived += death => died.Add(death.Entity);
            EntityId slime = client.CycleTarget(true);
            Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
            Assert.That(client.World.Remotes[slime].DefinitionId, Is.EqualTo(TrainingSlime), step);
            targets[client] = slime;
        }

        Assert.That(
            SocketClients.PumpUntil(() => clients.All(client => client.World.Target == targets[client]), clients),
            Is.True,
            $"{step}: the server confirmed each target");
        foreach (SocketClient client in clients)
        {
            client.AttackTarget();
        }

        bool isKilled = SocketClients.PumpUntil(
            () => clients.All(client => deaths[client].Contains(targets[client])),
            FightLimit,
            clients);
        Assert.That(isKilled, Is.True, $"{step}: each player's slime died");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.IsLocalDead, Is.False, $"{step}: both players survived");
        }
    }

    private static void AwaitConvergence(IAdminCommandService admin, string step, params SocketClient[] clients)
    {
        // The admin view is republished once a second, so agreement shows up within two of those.
        bool isConverged = SocketClients.PumpUntil(
            () => clients.All(client => HasConverged(client, clients, admin.GetPlayers(AdminActor.LocalConsole))),
            clients);
        string serverView = string.Join(
            ", ",
            admin.GetPlayers(AdminActor.LocalConsole).Select(player => $"{player.Entity.Value} {player.Position}"));
        Assert.That(isConverged, Is.True, $"{step}: every client agrees with the server at {serverView}");
    }

    // A client's own character where its prediction is, and every other character where it draws it.
    private static bool HasConverged(
        SocketClient client,
        IEnumerable<SocketClient> everyone,
        IReadOnlyList<PlayerSummary> server)
    {
        ClientWorld world = client.World;
        if (world.Predictor.PendingCount != 0)
        {
            return false;
        }

        foreach (SocketClient other in everyone)
        {
            EntityId entity = other.World.LocalEntity;
            PlayerSummary? summary = server.FirstOrDefault(player => player.Entity == entity);
            WorldPosition? drawn = ReferenceEquals(other, client) ? world.Predictor.Position : Draws(client, entity);
            if (summary == null || drawn == null || Horizontal(drawn.Value, summary.Position) > ConvergedDistance)
            {
                return false;
            }
        }

        return true;
    }

    private static WorldPosition? Draws(SocketClient viewer, EntityId entity)
    {
        ClientWorld world = viewer.World;
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote)
            && remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition position, out WorldDirection _)
                ? position
                : null;
    }

    private static WorldPosition PositionOf(IAdminCommandService admin, EntityId entity)
    {
        return admin.GetPlayers(AdminActor.LocalConsole).Single(player => player.Entity == entity).Position;
    }

    private static float Horizontal(WorldPosition a, WorldPosition b)
    {
        float deltaX = a.X - b.X;
        float deltaZ = a.Z - b.Z;
        return (float)Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static void AssertCleanTraffic(SocketClient client, string step)
    {
        Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
        Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
    }

    private static class Players
    {
        public const string FirstIdentity = "prototype-first";
        public const string FirstName = "ProtoFirst";
        public const string SecondIdentity = "prototype-second";
        public const string SecondName = "ProtoSecond";
    }

    [Test]
    public void TwoPlayers_OverRealSocketsAndPostgres_SeeEachOtherFightAndKeepTheirCharactersThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        Dictionary<string, WorldPosition> stopped;
        using (IHost first = StartHost(root.Path))
        {
            stopped = PlayTogether(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted, stopped);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
