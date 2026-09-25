using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Milestone 4 verification "database unavailability rejects durable mutations without false success"
///     (Persistence §9): the real PostgreSQL 18 container is paused, so connections neither succeed nor fail, as in a
///     real outage. Nothing durable is reported done, the world keeps running, and work resumes once it is back.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DatabaseOutageTests
{
    [TearDown]
    public void EndOutage()
    {
        Resume();
    }

    private const string SlimeGel = "item.material.slime_gel";
    private const int CommandTimeoutMs = 1000;
    private const string DatabaseAvailable = "\"database\":\"available\"";
    private const string DatabaseUnavailable = "\"database\":\"unavailable\"";

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;
    private bool m_isPaused;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    // The store as the host builds it: each connection bounded by the command timeout.
    private TestServer NewServer()
    {
        return new TestServer(
            persistence: new PersistenceOptions
            {
                CommandTimeoutMs = CommandTimeoutMs,
                MaxRetries = 1,
                RetryBaseDelayMs = 1
            },
            store: new PostgresGameStore(
                m_database.ConnectionString,
                TimeSpan.FromMilliseconds(CommandTimeoutMs)));
    }

    private void Pause()
    {
        m_database.Pause();
        m_isPaused = true;
    }

    private void Resume()
    {
        if (m_isPaused)
        {
            m_database.Resume();
            m_isPaused = false;
        }
    }

    // Paused, and known to be down: the first probe after the pause times out.
    private void StartOutage(TestServer server)
    {
        Pause();
        server.Persistence.Probe();
        Assert.That(server.Persistence.State, Is.EqualTo(DatabaseState.Unavailable));
    }

    private static ItemDropEntity DropNextTo(TestServer server, ConnectionId connection)
    {
        PlayerEntity player = server.PlayerOf(connection);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            2,
            new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private static bool WasToldOfAPickup(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlOpcodesSentTo(connection)
            .Any(opcode => opcode == MessageOpcode.ItemPickedUp || opcode == MessageOpcode.InventoryChanged);
    }

    private PickupResult? Ledger(ItemDropEntity drop, long character)
    {
        return m_store.FindPickupAsync(drop.DropId, character, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Test]
    public void AfterTheOutage_PickupsWorkAgainAndTheWaitingCheckpointLands()
    {
        TestServer server = NewServer();
        ConnectionId player = server.EnterWorldAs("outage-after", "OutageAfter");
        AccountId account = server.SessionOf(player).Account!.Value;
        long character = server.SessionOf(player).Character!.Character.Value;
        server.Place(player, 3.5f, 4.5f);
        WorldPosition moved = server.PlayerOf(player).Position;
        StartOutage(server);
        server.Disconnect(player);
        server.Tick(2);

        Resume();
        server.Tick(3);

        StoredCharacter stored = m_store.LoadCharacterAsync(account, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!;
        Assert.That(stored.Position, Is.EqualTo(moved), "the checkpoint queued during the outage was written");

        ConnectionId again = server.EnterWorldAs("outage-after", "OutageAfter");
        ItemDropEntity drop = DropNextTo(server, again);
        server.SendPickup(again, drop.Id, 1);
        server.TickUntil(() => !server.World.Maps.Single().Contains(drop.Id));

        Assert.That(Ledger(drop, character)!.Status, Is.EqualTo(PickupStatus.Committed));
        Assert.That(WasToldOfAPickup(server, again), Is.True);
    }

    [Test]
    public void DurableRequests_WhileDown_AreRefusedAndTheWorldKeepsTicking()
    {
        TestServer server = NewServer();
        ConnectionId player = server.EnterWorldAs("outage-requests", "OutageAsks");
        ConnectionId selecting = server.Connect();
        server.SignIn(selecting, "dev:outage-selecting");
        server.TickUntil(() => server.SessionOf(selecting).Characters != null);
        StartOutage(server);
        server.Transport.ClearSent();
        uint tickBefore = server.CurrentTick;

        server.SendLogout(player, 1);
        server.SendCreateCharacter(selecting, "OutageNew");
        ConnectionId newcomer = server.Connect();
        server.SendHello(newcomer);
        server.Tick(3);

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.InWorld), "the player stays");
        CreateCharacterResult.TryRead(
            server.Transport.ControlSentTo(selecting)
                .Single(message => message.Opcode == MessageOpcode.CreateCharacterResult)
                .Payload,
            out CreateCharacterResult created);
        Assert.That(created.Outcome, Is.EqualTo(CreateCharacterOutcome.ServiceUnavailable));
        Assert.That(server.Transport.Disconnects[newcomer], Is.EqualTo(DisconnectReason.ServerNotReady));
        Assert.That(server.CurrentTick, Is.EqualTo(tickBefore + 3));
        Assert.That(server.Transport.SnapshotsSentTo(player), Is.Not.Empty, "the world kept sending snapshots");
    }

    [Test]
    public void Operations_AgainstAFrozenDatabase_EndWithinAFewCommandTimeouts()
    {
        TestServer server = NewServer();
        server.EnterWorldAs("outage-bound", "OutageBound");
        var clock = Stopwatch.StartNew();

        StartOutage(server);
        long firstProbe = clock.ElapsedMilliseconds;
        clock.Restart();
        server.Tick();
        long nextTick = clock.ElapsedMilliseconds;

        // Npgsql's own defaults wait 15 s to connect and about a minute for a cancelled command on a frozen server.
        Assert.That(firstProbe, Is.LessThan(5 * CommandTimeoutMs), "the probe that found the outage");
        Assert.That(nextTick, Is.LessThan(5 * CommandTimeoutMs), "a tick whose probe finds it still down");
    }

    [Test]
    public void Pickup_WhileTheDatabaseIsKnownDown_IsRefusedAtOnceAndReservesNothing()
    {
        TestServer server = NewServer();
        ConnectionId picker = server.EnterWorldAs("outage-known", "OutageKnown");
        long character = server.SessionOf(picker).Character!.Character.Value;
        ItemDropEntity drop = DropNextTo(server, picker);
        StartOutage(server);
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 1);
        server.Tick();

        CommandRejected rejected = Rejections(server, picker).Single();
        Assert.That(rejected.Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(rejected.CommandSequence, Is.EqualTo(1u));
        Assert.That(drop.IsReserved, Is.False);
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True);
        Assert.That(WasToldOfAPickup(server, picker), Is.False);

        Resume();
        Assert.That(Ledger(drop, character), Is.Null, "no ledger row");
    }

    [Test]
    public void Pickup_WhoseCommitMeetsTheOutage_StaysReservedAndEndsAsServiceUnavailableWithoutALedgerRow()
    {
        TestServer server = NewServer();
        ConnectionId picker = server.EnterWorldAs("outage-inflight", "OutageFlight");
        long character = server.SessionOf(picker).Character!.Character.Value;
        ItemDropEntity drop = DropNextTo(server, picker);
        server.Transport.ClearSent();
        Pause();

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(3);

        Assert.That(server.Persistence.State, Is.EqualTo(DatabaseState.Unavailable));
        Assert.That(drop.ReservedBy, Is.EqualTo(new CharacterId(character)), "unsettled until the ledger answers");
        Assert.That(Rejections(server, picker), Is.Empty);
        Assert.That(WasToldOfAPickup(server, picker), Is.False);

        Resume();
        server.TickUntil(() => !drop.IsReserved);

        Assert.That(Rejections(server, picker).Single().Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True);
        Assert.That(WasToldOfAPickup(server, picker), Is.False);
        Assert.That(Ledger(drop, character), Is.Null);
    }

    [Test]
    public void Readiness_OfAHostOnThePausedDatabase_FollowsItDownAndBack()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0",
                "--Health:Enabled=true",
                "--Persistence:IdleProbeIntervalMs=100",
                $"--Persistence:CommandTimeoutMs={CommandTimeoutMs}"
            },
            root.Path,
            m_database.ConnectionString);
        using IHost host = builder.Build();
        host.Start();
        using HttpClient http = HealthTests.ClientFor(host);

        bool isReady = HealthTests.WaitFor(http, "/health/ready", HttpStatusCode.OK, DatabaseAvailable);
        Pause();
        bool isUnready = HealthTests.WaitFor(
            http,
            "/health/ready",
            HttpStatusCode.ServiceUnavailable,
            DatabaseUnavailable);
        (HttpStatusCode live, string liveBody) = HealthTests.Get(http, "/health/live");
        Resume();
        bool isReadyAgain = HealthTests.WaitFor(http, "/health/ready", HttpStatusCode.OK, DatabaseAvailable);
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isReady, Is.True, "ready while the database answers");
        Assert.That(isUnready, Is.True, "the idle writer's probe met the paused database, and readiness dropped");
        Assert.That(live, Is.EqualTo(HttpStatusCode.OK), $"still live: {liveBody}");
        Assert.That(isReadyAgain, Is.True, "ready again once the database answers");
    }
}
}
