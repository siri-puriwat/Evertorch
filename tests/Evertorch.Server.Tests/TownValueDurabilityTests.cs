using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
///     Retries cannot duplicate the town's value on PostgreSQL 18 (Milestone 7 verification line V3, Persistence
///     §5–§7): buys, sales, and turn-ins asked for twice or whose answers are lost are each committed and paid
///     once; coins stay between 0 and their cap; and a logout, a removal, a crossing, and a clean stop wait for a
///     trade or a turn-in in flight and keep it once. The servers run on the real store.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TownValueDurabilityTests
{
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Hunt = "quest.crawler_hunt";
    private const string Potion = "item.consumable.minor_health";
    private const string Sword = "item.weapon.training_sword";
    private const string Gel = "item.material.slime_gel";
    private const long Cap = 1_000_000_000;
    private const string StopIdentity = "v3-stop";
    private const string StopName = "TownStop";

    // The turn-in's commit, its wait for the held row, the join, and the drain are each bounded by the command timeout,
    // so it must outlast the test's hold with room to spare.
    private const int StopCommandTimeoutMs = 30000;

    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
    private static readonly TimeSpan StopLimit = TimeSpan.FromSeconds(60);

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

    private TestServer NewServer(IGameStore? store = null, bool withEveryMap = false)
    {
        return new TestServer(
            store: store ?? new PostgresGameStore(m_database.ConnectionString),
            withEveryMap: withEveryMap,
            withNpcs: true);
    }

    private long Scalar(string sql)
    {
        using (var connection = new NpgsqlConnection(m_database.ConnectionString))
        {
            connection.Open();
            return Scalar(connection, sql);
        }
    }

    private static long Scalar(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    private void Execute(string sql)
    {
        using (var connection = new NpgsqlConnection(m_database.ConnectionString))
        using (var command = new NpgsqlCommand(sql, connection))
        {
            connection.Open();
            command.ExecuteNonQuery();
        }
    }

    private long LedgerRows(long character, string operation)
    {
        return Scalar(
            $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
            + $"AND operation_type = '{operation}'");
    }

    private long CoinsOf(long character)
    {
        return Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private long HeldOf(long character, string item)
    {
        return Scalar(
            "SELECT coalesce(sum(quantity), 0) FROM inventory_items "
            + $"WHERE character_id = {character} AND item_definition_id = '{item}'");
    }

    private bool IsCompleted(long character)
    {
        return Scalar(
            "SELECT count(*) FROM character_quests "
            + $"WHERE character_id = {character} AND quest_definition_id = '{Hunt}' AND state = 'completed'") == 1;
    }

    // Signs in as dev:<identity> and creates the character, then writes what earlier sessions would have left it
    // straight to the database: coins, a stack of gel, and the hunt at a progress.
    private long CreateSeeded(
        TestServer server,
        ConnectionId connection,
        string identity,
        string name,
        long coins,
        int gels,
        int? progress)
    {
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        if (gels > 0)
        {
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"VALUES ({character}, '{Gel}', {gels}, 0, 0)");
        }

        if (progress != null)
        {
            Execute(
                "INSERT INTO character_quests "
                + "(character_id, quest_definition_id, state, progress, started_at, version) "
                + $"VALUES ({character}, '{Hunt}', 'active', {progress.Value}, now(), 0)");
        }

        return character;
    }

    private ConnectionId EnterSeeded(
        TestServer server,
        string identity,
        string name,
        long coins,
        int gels = 0,
        int? progress = null)
    {
        ConnectionId connection = server.Connect();
        long character = CreateSeeded(server, connection, identity, name, coins, gels, progress);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    // Places the player the given distance east of the NPC, within its reach, and lets it know the NPC.
    private static EntityId StandBy(TestServer server, ConnectionId player, string npc, float distance = 2f)
    {
        NpcEntity entity = server.NpcOf(npc);
        server.Place(player, entity.Position.X + distance, entity.Position.Z);
        server.Tick(2);
        Assert.That(server.SessionOf(player).KnownEntities.Contains(entity.Id), Is.True, $"{npc} known");
        return entity.Id;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static long CharacterIdOf(TestServer server, ConnectionId player)
    {
        return CharacterOf(server, player).Character.Value;
    }

    private static long RowOf(TestServer server, ConnectionId player, string item)
    {
        return CharacterOf(server, player).Inventory.Rows.Single(row => row.Item.Value == item).InventoryItem;
    }

    private static bool IsInTheWorld(TestServer server, long character)
    {
        return server.World.Maps.Any(map => map.Players.Any(player => player.Character.Value == character));
    }

    private static MapDefinitionId? MapOf(TestServer server, long character)
    {
        return server.World.Maps
            .Where(map => map.Players.Any(player => player.Character.Value == character))
            .Select(map => (MapDefinitionId?)map.Definition.Id)
            .SingleOrDefault();
    }

    private static int Changes(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlOpcodesSentTo(player).Count(opcode => opcode == MessageOpcode.InventoryChanged);
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read)
                ? (read.CommandSequence, read.Reason)
                : (0u, CommandRejectionReason.None))
            .ToArray();
    }

    // Ticks until the player's inventory operation, already begun, has settled.
    private static void Settle(TestServer server, ConnectionId player)
    {
        for (int tick = 0; tick < 200 && CharacterOf(server, player).Operation != null; tick++)
        {
            server.Tick();
        }

        Assert.That(CharacterOf(server, player).Operation, Is.Null, "the operation settled");
    }

    // Creates the character through a server on the same database, then writes the ready hunt and the place where the
    // field's portal lands a player, by the Gate Warden, straight to the database, as a hunt and a crossing back would
    // have left them.
    private long CreateReadyBesideTheGateWarden()
    {
        TestServer setup = NewServer();
        long character = CreateSeeded(setup, setup.Connect(), StopIdentity, StopName, 0, 0, 5);
        Execute($"UPDATE characters SET position_x = 20.5, position_z = 0 WHERE id = {character}");
        return character;
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
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private static SocketClient EnterWorld(IHost host)
    {
        var client = new SocketClient(host.Services.GetRequiredService<ServerContent>(), StopIdentity, StopName);
        client.EnterWorld(host.Services.GetRequiredService<IServerTransport>().LocalPort);
        return client;
    }

    // Polled on a connection of its own and outside any transaction: inside one, pg_stat_activity keeps the snapshot of
    // its first read.
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

    [Test]
    public void BuySellAndTurnIn_AskedForTwiceInATick_AreEachCommittedOnce_AndTheQuestPaysOnce()
    {
        TestServer server = NewServer();
        ConnectionId player = EnterSeeded(server, "v3-twice", "TownTwice", 100, 6, 5);
        long character = CharacterIdOf(server, player);
        long gels = RowOf(server, player, Gel);
        EntityId quartermaster = StandBy(server, player, Quartermaster);
        server.Transport.ClearSent();

        server.SendBuy(player, quartermaster, Sword, 1, 1);
        server.SendBuy(player, quartermaster, Sword, 1, 2);
        server.Tick();
        Settle(server, player);
        server.SendSell(player, quartermaster, gels, 3, 3);
        server.SendSell(player, quartermaster, gels, 3, 4);
        server.Tick();
        Settle(server, player);
        EntityId warden = StandBy(server, player, GateWarden);
        server.SendCompleteQuest(player, warden, Hunt, 5);
        server.SendCompleteQuest(player, warden, Hunt, 6);
        server.Tick();
        Settle(server, player);
        server.SendCompleteQuest(player, warden, Hunt, 7);
        server.Tick();
        Settle(server, player);

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (4u, CommandRejectionReason.ItemActionInFlight),
                    (6u, CommandRejectionReason.ItemActionInFlight), (7u, CommandRejectionReason.NotAllowedNow)
                }),
            "each repeat refused while the first was in flight, and a completed quest not turned in again");
        Assert.That(
            (LedgerRows(character, "buy"), LedgerRows(character, "sell"), LedgerRows(character, "quest_reward")),
            Is.EqualTo((1L, 1L, 1L)),
            "one ledger row each");
        Assert.That(CoinsOf(character), Is.EqualTo(100 - 50 + 3 * 2 + 100), "one sword, three gels, one reward");
        Assert.That((HeldOf(character, Sword), HeldOf(character, Gel)), Is.EqualTo((1L, 3L)));
        Assert.That(IsCompleted(character), Is.True);
        Assert.That(
            Scalar($"SELECT base_level FROM characters WHERE id = {character}"),
            Is.EqualTo(3),
            "rewarded once");
    }

    [Test]
    public void BuySellAndTurnIn_WhoseAnswersAreLost_AreEachSettledFromTheLedgerOnce()
    {
        var store = new LosingAnswersGameStore(new PostgresGameStore(m_database.ConnectionString));
        TestServer server = NewServer(store);
        ConnectionId player = EnterSeeded(server, "v3-lost", "TownLost", 100, 3, 5);
        long character = CharacterIdOf(server, player);
        long gels = RowOf(server, player, Gel);
        EntityId quartermaster = StandBy(server, player, Quartermaster);
        server.Transport.ClearSent();

        store.IsLosingAnswers = true;
        server.SendBuy(player, quartermaster, Potion, 1, 1);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        store.IsLosingAnswers = true;
        server.SendSell(player, quartermaster, gels, 3, 2);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        EntityId warden = StandBy(server, player, GateWarden);
        store.IsLosingAnswers = true;
        server.SendCompleteQuest(player, warden, Hunt, 3);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);

        Assert.That(store.LostAnswers, Is.GreaterThanOrEqualTo(3), "every commit lost at least one answer");
        Assert.That(
            (LedgerRows(character, "buy"), LedgerRows(character, "sell"), LedgerRows(character, "quest_reward")),
            Is.EqualTo((1L, 1L, 1L)),
            "one ledger row each");
        Assert.That(CoinsOf(character), Is.EqualTo(100 - 20 + 3 * 2 + 100), "the price, the sale, and the reward once");
        Assert.That(CharacterOf(server, player).Inventory.Coins, Is.EqualTo(186L));
        Assert.That((HeldOf(character, Potion), HeldOf(character, Gel)), Is.EqualTo((1L, 0L)));
        Assert.That(IsCompleted(character), Is.True);
        Assert.That(
            Scalar($"SELECT base_level * 1000 + base_exp FROM characters WHERE id = {character}"),
            Is.EqualTo(3070),
            "150 base experience once: level 3 with 70");
        Assert.That(server.PlayerOf(player).Level, Is.EqualTo(3));
        Assert.That(Changes(server, player), Is.EqualTo(3), "each told once");
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void CleanStop_WithATurnInInFlight_CommitsItOnceForTheNextHost()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        long character = CreateReadyBesideTheGateWarden();
        using (IHost first = StartHost(root.Path))
        using (SocketClient client = EnterWorld(first))
        {
            ClientWorld world = client.World;
            Assert.That(
                client.PumpUntil(() => world.Quests.Any(entry => entry.Progress == 5)
                    && world.Remotes.Values.Any(remote => remote.DefinitionId == GateWarden)),
                Is.True,
                "the ready quest and the Gate Warden");
            RemoteEntity warden = world.Remotes.Values.Single(remote => remote.DefinitionId == GateWarden);
            int windows = client.NpcWindows.Count;
            client.TalkTo(warden.Entity);
            Assert.That(client.PumpUntil(() => client.NpcWindows.Count > windows), Is.True, "walked up to it");
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
                    client.Connection.SendCompleteQuest(warden.Entity, new QuestDefinitionId(Hunt));
                    Assert.That(PumpUntilBlocked(client, watcher, holder), Is.True, "the commit waits for the row");

                    first.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
                    var stopping = Task.Run(() => first.StopAsync());
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
            Assert.That(world.Inventory.Coins, Is.Zero, "the client never heard of the reward");
        }

        Assert.That(
            (LedgerRows(character, "quest_reward"), CoinsOf(character), IsCompleted(character)),
            Is.EqualTo((1L, 100L, true)),
            "the drain let the commit through, once");
        Assert.That(
            Scalar($"SELECT base_level * 1000 + base_exp FROM characters WHERE id = {character}"),
            Is.EqualTo(3070),
            "the stop's checkpoint left the reward's level and experience alone");

        using (IHost second = StartHost(root.Path))
        using (SocketClient again = EnterWorld(second))
        {
            ClientWorld world = again.World;
            Assert.That(
                again.PumpUntil(() => world.Quests.Any(entry => entry.State == QuestState.Completed)),
                Is.True,
                "the next host's baseline holds the quest completed");
            Assert.That((world.Level, world.Inventory.Coins), Is.EqualTo(((ushort)3, 100u)));
            second.StopAsync().GetAwaiter().GetResult();
        }

        Assert.That((LedgerRows(character, "quest_reward"), CoinsOf(character)), Is.EqualTo((1L, 100L)), "still once");
    }

    [Test]
    public void Coins_NeverGoBelowZeroOrPastTheirCap()
    {
        TestServer server = NewServer();
        ConnectionId poor = EnterSeeded(server, "v3-poor", "TownPoor", 49);
        ConnectionId rich = EnterSeeded(server, "v3-rich", "TownRich", Cap - 5, 6);
        long poorCharacter = CharacterIdOf(server, poor);
        long richCharacter = CharacterIdOf(server, rich);
        long gels = RowOf(server, rich, Gel);
        EntityId quartermaster = StandBy(server, poor, Quartermaster);
        StandBy(server, rich, Quartermaster, 2.5f);
        server.Transport.ClearSent();

        server.SendBuy(poor, quartermaster, Sword, 1, 1);
        server.SendSell(rich, quartermaster, gels, 3, 1);
        server.Tick();
        Settle(server, poor);
        Settle(server, rich);
        server.SendSell(rich, quartermaster, gels, 2, 2);
        server.Tick();
        Settle(server, rich);

        Assert.That(Rejections(server, poor), Is.EqualTo(new[] { (1u, CommandRejectionReason.NotEnoughCoins) }));
        Assert.That(
            Rejections(server, rich),
            Is.EqualTo(new[] { (1u, CommandRejectionReason.CoinCapReached) }),
            "six coins more than the cap allows; four do not pass it");
        Assert.That((CoinsOf(poorCharacter), CoinsOf(richCharacter)), Is.EqualTo((49L, Cap - 1)));
        Assert.That((LedgerRows(poorCharacter, "buy"), LedgerRows(richCharacter, "sell")), Is.EqualTo((0L, 1L)));
        Action belowZero = () => Execute($"UPDATE characters SET currency = -1 WHERE id = {poorCharacter}");
        Action pastTheCap = () => Execute($"UPDATE characters SET currency = {Cap + 1} WHERE id = {richCharacter}");
        Assert.That(belowZero, Throws.InstanceOf<PostgresException>(), "the database refuses coins below 0");
        Assert.That(pastTheCap, Throws.InstanceOf<PostgresException>(), "and past the cap");
    }

    [Test]
    public void Crossing_WithATurnInInFlight_WaitsOnTheGround_AndCrossesOnceItSettles()
    {
        TestServer server = NewServer(withEveryMap: true);
        ConnectionId player = EnterSeeded(server, "v3-crossing", "TownCrossing", 0, progress: 5);
        long character = CharacterIdOf(server, player);
        EntityId warden = StandBy(server, player, GateWarden);
        server.RunsPersistence = false;

        server.SendCompleteQuest(player, warden, Hunt, 1);
        server.Tick();
        server.World.TryGetMap(Ground, out MapInstance? ground);
        server.Place(player, ground!.Definition.Portals.Single().Center.X, ground.Definition.Portals.Single().Center.Z);
        server.Tick(5);
        MapDefinitionId? waitedOn = MapOf(server, character);
        server.RunsPersistence = true;
        server.TickUntil(() => MapOf(server, character) == Field);

        Assert.That(waitedOn, Is.EqualTo(Ground), "the crossing waited for the turn-in");
        Assert.That(
            (LedgerRows(character, "quest_reward"), CoinsOf(character), IsCompleted(character)),
            Is.EqualTo((1L, 100L, true)),
            "paid once");
        Assert.That(server.PlayerOf(player).Level, Is.EqualTo(3), "and the level with it");
    }

    [Test]
    public void Logout_AndRemoval_WithATradeInFlight_WaitForItsCommit_AndKeepItOnce()
    {
        TestServer server = NewServer();
        ConnectionId leaving = EnterSeeded(server, "v3-logout", "TownLogout", 100);
        ConnectionId dropped = EnterSeeded(server, "v3-removal", "TownRemoval", 0, 3);
        long leavingCharacter = CharacterIdOf(server, leaving);
        long droppedCharacter = CharacterIdOf(server, dropped);
        long gels = RowOf(server, dropped, Gel);
        EntityId quartermaster = StandBy(server, leaving, Quartermaster);
        StandBy(server, dropped, Quartermaster, 2.5f);
        server.RunsPersistence = false;

        server.SendBuy(leaving, quartermaster, Potion, 1, 1);
        server.SendSell(dropped, quartermaster, gels, 3, 1);
        server.Tick();
        server.SendLogout(leaving, 2);
        server.Disconnect(dropped);
        server.Tick(5);
        bool[] waited = { IsInTheWorld(server, leavingCharacter), IsInTheWorld(server, droppedCharacter) };
        int queued = server.Persistence.WaitingCheckpoints;
        server.RunsPersistence = true;
        server.TickUntil(() => !IsInTheWorld(server, leavingCharacter) && !IsInTheWorld(server, droppedCharacter));

        Assert.That(waited, Is.EqualTo(new[] { true, true }), "both stayed while their commits waited");
        Assert.That(queued, Is.Zero, "neither queued its final checkpoint before its commit settled");
        Assert.That(
            (CoinsOf(leavingCharacter), HeldOf(leavingCharacter, Potion), LedgerRows(leavingCharacter, "buy")),
            Is.EqualTo((80L, 1L, 1L)),
            "the logout kept the potion once");
        Assert.That(
            (CoinsOf(droppedCharacter), HeldOf(droppedCharacter, Gel), LedgerRows(droppedCharacter, "sell")),
            Is.EqualTo((6L, 0L, 1L)),
            "the removal kept the sale once");

        TestServer next = NewServer();
        ConnectionId again = next.EnterWorldAs("v3-logout", "TownLogout");
        Assert.That(CharacterOf(next, again).Inventory.Coins, Is.EqualTo(80L), "the next server loads it once");
        Assert.That(CharacterOf(next, again).Inventory.Rows.Single().Quantity, Is.EqualTo(1u));
    }
}
}
