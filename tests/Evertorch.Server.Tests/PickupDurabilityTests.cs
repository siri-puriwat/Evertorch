using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A pickup is in PostgreSQL before any client hears of it, it outlives the server process, and neither a save nor
///     a clean stop while its commit is in flight adds it twice (Gameplay Systems §12 steps 8–10; Persistence §5, §6,
///     §11). The servers share one real PostgreSQL 18 database.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PickupDurabilityTests
{
    private const string SlimeGel = "item.material.slime_gel";
    private const uint GelAmount = 2;
    private const string StopIdentity = "durable-stop";
    private const string StopName = "DurableStop";

    // The pickup job, its wait for the held row, the join, and the drain are each bounded by the command timeout, so
    // it must outlast the test's hold with room to spare.
    private const int StopCommandTimeoutMs = 30000;

    private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(60);

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;

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

    private TestServer NewServer()
    {
        return new TestServer(store: new PostgresGameStore(m_database.ConnectionString));
    }

    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0",
                "--DevelopmentAuthentication:Enabled=true",
                $"--Persistence:CommandTimeoutMs={StopCommandTimeoutMs}"
            },
            contentRootPath,
            m_database.ConnectionString);
        builder.Services.AddSingleton<ITickPhase>(services =>
            new GelSpawner(services.GetRequiredService<WorldSimulation>()));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private static ItemDropEntity DropNextTo(TestServer server, ConnectionId connection, uint amount)
    {
        PlayerEntity player = server.PlayerOf(connection);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            amount,
            new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    private static bool HasBeenTold(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlOpcodesSentTo(connection)
            .Any(opcode => opcode == MessageOpcode.ItemPickedUp || opcode == MessageOpcode.InventoryChanged);
    }

    private static InventorySnapshot BaselineOf(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.InventorySnapshot)
            .Select(message =>
            {
                InventorySnapshot.TryRead(message.Payload, out InventorySnapshot? part);
                return part!;
            })
            .Single();
    }

    private InventoryResult? Ledger(ItemDropEntity drop, long character)
    {
        return m_store.FindOperationAsync(drop.DropId, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private AccountId AccountOf(string identity)
    {
        // The development validator provisions by the normalized login; provisioning again returns the same account.
        AccountId? account = m_store
            .ProvisionAccountAsync(
                DevelopmentTokenValidator.NormalizedLogin($"dev:{identity}"),
                DateTime.UtcNow,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(account, Is.Not.Null, "the account exists and is active");
        return account!.Value;
    }

    private StoredCharacter Stored(AccountId account, long character)
    {
        StoredCharacter? stored = m_store
            .LoadCharacterAsync(account, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(stored, Is.Not.Null, "the character is stored");
        return stored!;
    }

    private static long GelIn(StoredCharacter stored)
    {
        return stored.Items.Where(item => item.ItemDefinitionId == SlimeGel).Sum(item => (long)item.Quantity);
    }

    private static long GelIn(ClientWorld world)
    {
        return world.Inventory.Rows.Where(row => row.Item == new ItemDefinitionId(SlimeGel)).Sum(row => row.Quantity);
    }

    private long LedgerRowsOf(long character)
    {
        using (var connection = new NpgsqlConnection(m_database.ConnectionString))
        {
            connection.Open();
            return Scalar(connection, $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character}");
        }
    }

    private static long Scalar(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    // Polled on a connection of its own and outside any transaction: inside one, pg_stat_activity keeps the snapshot
    // of its first read.
    private static bool PumpUntilBlocked(SocketClient client, NpgsqlConnection watcher, long holder)
    {
        string waiting = $"SELECT count(*) FROM pg_stat_activity WHERE {holder} = ANY(pg_blocking_pids(pid))";
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < SocketClient.Limit)
        {
            client.PumpFor(TimeSpan.FromMilliseconds(50));
            if (Scalar(watcher, waiting) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Task StopInBackground(IHost host)
    {
        return Task.Run(() => host.StopAsync());
    }

    private static SocketClient EnterWorld(IHost host)
    {
        var client = new SocketClient(host.Services.GetRequiredService<ServerContent>(), StopIdentity, StopName);
        client.EnterWorld(host.Services.GetRequiredService<IServerTransport>().LocalPort);
        return client;
    }

    // Lays a gel beside a player on the tick thread when the test asks, as a drop roll would.
    private sealed class GelSpawner : ITickPhase
    {
        private readonly WorldSimulation m_world;
        private long m_player;

        public GelSpawner(WorldSimulation world)
        {
            m_world = world;
        }

        public TickPhase Phase => TickPhase.Movement;

        public void Execute(in TickContext context)
        {
            long player = Interlocked.Exchange(ref m_player, 0);
            // The real host loads every map; the players of this test enter the training ground.
            m_world.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? map);
            if (player != 0
                && map != null
                && map.TryGetPlayer(new EntityId(player), out PlayerEntity? entity)
                && entity != null)
            {
                m_world.SpawnItemDrop(
                    map,
                    new ItemDefinitionId(SlimeGel),
                    GelAmount,
                    new WorldPosition(entity.Position.X + 1f, entity.Position.Y, entity.Position.Z),
                    context.Tick,
                    long.MaxValue,
                    default);
            }
        }

        public void Request(EntityId player)
        {
            Interlocked.Exchange(ref m_player, player.Value);
        }
    }

    [Test]
    public void CleanStop_WithAPickupInFlight_CommitsItOnceForTheNextHost()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        long character;
        using (IHost first = StartHost(root.Path))
        using (SocketClient client = EnterWorld(first))
        {
            character = client.Connection.Characters.Single().Character.Value;
            ClientWorld world = client.World;
            first.Services.GetServices<ITickPhase>().OfType<GelSpawner>().Single().Request(world.LocalEntity);
            RemoteEntity? gel = null;
            bool isDropped = client.PumpUntil(() =>
                (gel = world.Remotes.Values.FirstOrDefault(remote => remote.Kind == EntityKind.ItemDrop)) != null);
            Assert.That(isDropped, Is.True, "the gel lies beside the player");
            ServerLifetimeService lifetime = first.Services.GetRequiredService<ServerLifetimeService>();
            PersistenceWorker persistence = first.Services.GetRequiredService<PersistenceWorker>();

            using (var hold = new NpgsqlConnection(m_database.ConnectionString))
            using (var watcher = new NpgsqlConnection(m_database.ConnectionString))
            {
                hold.Open();
                watcher.Open();
                using (NpgsqlTransaction transaction = hold.BeginTransaction())
                {
                    long holder = Scalar(hold, "SELECT pg_backend_pid()", transaction);
                    Scalar(hold, $"SELECT id FROM characters WHERE id = {character} FOR UPDATE", transaction);
                    client.Connection.SendPickup(gel!.Entity);
                    bool isWaiting = PumpUntilBlocked(client, watcher, holder);
                    Assert.That(isWaiting, Is.True, "the commit waits for the held row");

                    first.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    Task stopping = StopInBackground(first);
                    Assert.That(
                        client.PumpUntil(() => !lifetime.IsSimulationRunning && persistence.WaitingCheckpoints > 0),
                        Is.True,
                        "the world stopped and queued its checkpoint while the commit waited");
                    transaction.Commit();
                    Assert.That(stopping.Wait(StopLimit), Is.True, "the stop ended once the commit went through");
                }
            }

            Assert.That(lifetime.HasFailed, Is.False, "a clean stop");
            Assert.That(
                client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected),
                Is.True,
                "the client heard the server stop");
            Assert.That(client.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance));
            Assert.That(world.Inventory.Revision, Is.Zero, "the client never heard of the pickup");
            Assert.That(world.Remotes.ContainsKey(gel!.Entity), Is.True, "nor saw the gel leave");
        }

        AccountId account = AccountOf(StopIdentity);
        Assert.That(GelIn(Stored(account, character)), Is.EqualTo(GelAmount), "the drain let the commit through");
        Assert.That(LedgerRowsOf(character), Is.EqualTo(1), "once");

        using (IHost second = StartHost(root.Path))
        using (SocketClient again = EnterWorld(second))
        {
            Assert.That(again.Connection.Characters.Single().Character.Value, Is.EqualTo(character));
            Assert.That(GelIn(again.World), Is.EqualTo(GelAmount), "the next host's baseline holds the gel once");
            second.StopAsync().GetAwaiter().GetResult();
        }

        Assert.That(GelIn(Stored(account, character)), Is.EqualTo(GelAmount), "its stop wrote no inventory");
    }

    [Test]
    public void Pickup_IsInTheLedgerBeforeAnyClientIsTold()
    {
        TestServer server = NewServer();
        ConnectionId picker = server.EnterWorldAs("durable-order", "DurableOrder");
        long character = server.SessionOf(picker).Character!.Character.Value;
        ItemDropEntity drop = DropNextTo(server, picker, 2);
        server.Transport.ClearSent();
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.Tick(10);

        Assert.That(HasBeenTold(server, picker), Is.False, "nothing is said while the commit is held");
        Assert.That(Ledger(drop, character), Is.Null);

        server.RunsPersistence = true;
        for (int tick = 0; tick < 10 && !HasBeenTold(server, picker); tick++)
        {
            server.Tick();
            if (HasBeenTold(server, picker))
            {
                Assert.That(Ledger(drop, character)!.Status, Is.EqualTo(InventoryStatus.Committed));
            }
        }

        Assert.That(HasBeenTold(server, picker), Is.True);
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }

    [Test]
    public void Pickup_SurvivesTheLossOfTheServerProcess()
    {
        TestServer lost = NewServer();
        ConnectionId picker = lost.EnterWorldAs("durable-loss", "DurableLoss");
        ItemDropEntity drop = DropNextTo(lost, picker, 2);
        lost.SendPickup(picker, drop.Id, 1);
        lost.TickUntil(() => lost.Transport.ControlOpcodesSentTo(picker).Contains(MessageOpcode.InventoryChanged));

        TestServer restarted = NewServer();
        ConnectionId again = restarted.EnterWorldAs("durable-loss", "DurableLoss");

        InventorySnapshot snapshot = BaselineOf(restarted, again);
        Assert.That(snapshot.Revision, Is.EqualTo(1u));
        InventoryEntry row = snapshot.Entries.Single();
        Assert.That(row.Item, Is.EqualTo(new ItemDefinitionId(SlimeGel)));
        Assert.That(row.Quantity, Is.EqualTo(2u));
    }

    [Test]
    public void Save_WithAPickupInFlight_KeepsTheGelOnce()
    {
        TestServer server = NewServer();
        ConnectionId picker = server.EnterWorldAs("durable-save", "DurableSave");
        long character = server.SessionOf(picker).Character!.Character.Value;
        AccountId account = server.SessionOf(picker).Account!.Value;
        ItemDropEntity drop = DropNextTo(server, picker, GelAmount);
        server.Transport.ClearSent();
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.Tick();

        Task<int> saved = server.Admin.SaveAsync(AdminActor.LocalConsole);
        server.Tick();

        Assert.That(saved.IsCompletedSuccessfully, Is.True, "a tick ran the save");
        Assert.That(saved.Result, Is.EqualTo(1), "the save queued the picker's checkpoint beside the commit");
        Assert.That(HasBeenTold(server, picker), Is.False, "nothing is said while the commit waits");
        Assert.That(Ledger(drop, character), Is.Null);

        server.RunsPersistence = true;
        server.Tick(10);

        IReadOnlyList<MessageOpcode> told = server.Transport.ControlOpcodesSentTo(picker);
        Assert.That(told.Count(opcode => opcode == MessageOpcode.ItemPickedUp), Is.EqualTo(1), "told once");
        Assert.That(told.Count(opcode => opcode == MessageOpcode.InventoryChanged), Is.EqualTo(1), "changed once");
        Assert.That(server.Persistence.WaitingCheckpoints, Is.Zero, "the save's checkpoint was written too");
        Assert.That(Ledger(drop, character)!.Status, Is.EqualTo(InventoryStatus.Committed));
        StoredCharacter stored = Stored(account, character);
        Assert.That(stored.InventoryRevision, Is.EqualTo(1u));
        Assert.That(GelIn(stored), Is.EqualTo(GelAmount));

        TestServer restarted = NewServer();
        InventorySnapshot baseline = BaselineOf(restarted, restarted.EnterWorldAs("durable-save", "DurableSave"));
        Assert.That(baseline.Revision, Is.EqualTo(1u), "the next server loads the gel once");
        Assert.That(baseline.Entries.Single().Quantity, Is.EqualTo(GelAmount));
    }
}
}
