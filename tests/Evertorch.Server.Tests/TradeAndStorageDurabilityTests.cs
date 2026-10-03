using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Trades and storage on PostgreSQL 18 (Milestone 14 verification; Gameplay Systems §11.4, §16; Persistence §5, §7,
///     §9): lost answers settled once from <c>trades</c> and the ledger, a database known down leaving a trade unsaved
///     and one paused mid-commit holding it, a full bag and the coin cap ending a trade with nothing moved, a logout,
///     an expulsion, and a replaced connection waiting for a commit, a refine level kept through a trade, a deposit,
///     and a withdrawal, and a restart loading every result.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TradeAndStorageDurabilityTests
{
    [TearDown]
    public void EndOutage()
    {
        if (m_isPaused)
        {
            m_database.Resume();
            m_isPaused = false;
        }
    }

    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.iron_sword";
    private const string Storekeeper = "npc.storekeeper";
    private const int CommandTimeoutMs = 1000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private PostgresFixture m_database = null!;
    private bool m_isPaused;

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

    private sealed class Pair
    {
        public Pair(TestServer server, TradeRig rig, Trader anna, Trader bobby)
        {
            Server = server;
            Rig = rig;
            Anna = anna;
            Bobby = bobby;
        }

        public TestServer Server { get; }

        public TradeRig Rig { get; }

        public Trader Anna { get; }

        public Trader Bobby { get; }
    }

    private sealed class Trader
    {
        public Trader(string identity, string name, long character, ConnectionId connection)
        {
            Identity = identity;
            Name = name;
            Character = character;
            Connection = connection;
        }

        public string Identity { get; }

        public string Name { get; }

        public long Character { get; }

        public ConnectionId Connection { get; }
    }

    private TestServer NewServer(IGameStore? store = null, int reconnectGraceMs = 0)
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: store ?? NewStore(),
            reconnectGraceMs: reconnectGraceMs,
            withNpcs: true);
    }

    private PostgresGameStore NewStore()
    {
        return new PostgresGameStore(m_database.ConnectionString, TimeSpan.FromMilliseconds(CommandTimeoutMs));
    }

    private long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    // The character's stored bag and coins, "item quantity" by item, then the coins.
    private string Bag(long character)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT coalesce(string_agg(item_definition_id || ' ' || quantity, ', ' ORDER BY item_definition_id), '') "
            + $"|| '; ' || (SELECT currency FROM characters WHERE id = {character}) FROM inventory_items "
            + $"WHERE character_id = {character}",
            connection);
        return (string)command.ExecuteScalar()!;
    }

    private void Pause()
    {
        m_database.Pause();
        m_isPaused = true;
    }

    private void Resume()
    {
        m_database.Resume();
        m_isPaused = false;
    }

    private static void TickUntil(TestServer server, Func<bool> condition, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Limit)
        {
            server.Tick();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail($"Not reached within {Limit.TotalSeconds} s: {what}");
    }

    private static long Create(TestServer server, string identity, string name)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        server.Disconnect(connection);
        server.Tick();
        return character;
    }

    private static ConnectionId Enter(TestServer server, string identity, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId connection)
    {
        return server.SessionOf(connection).Character!;
    }

    private static long RowOf(TestServer server, ConnectionId connection, string item)
    {
        return CharacterOf(server, connection).Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    // Anna, stored holding ten gel, an Iron Sword, and 300 coins, and Bobby, of another account, with 50 coins, enter
    // beside each other at the spawn point.
    private Pair EnterPair(string tag, IGameStore? store = null, Action<long, long>? prepare = null)
    {
        TestServer server = NewServer(store);
        string annaName = $"Anna{tag}";
        string bobbyName = $"Bobby{tag}";
        long anna = Create(server, $"v14-{tag}-a", annaName);
        long bobby = Create(server, $"v14-{tag}-b", bobbyName);
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({anna}, '{Gel}', 10, 0, 0), ({anna}, '{Sword}', 1, 0, 0)");
        Execute($"UPDATE characters SET currency = 300 WHERE id = {anna}");
        Execute($"UPDATE characters SET currency = 50 WHERE id = {bobby}");
        prepare?.Invoke(anna, bobby);
        ConnectionId annaConnection = Enter(server, $"v14-{tag}-a", anna);
        ConnectionId bobbyConnection = Enter(server, $"v14-{tag}-b", bobby);
        return new Pair(
            server,
            new TradeRig(server),
            new Trader($"v14-{tag}-a", annaName, anna, annaConnection),
            new Trader($"v14-{tag}-b", bobbyName, bobby, bobbyConnection));
    }

    // Anna offers four gel and 100 coins, Bobby 20 coins; both lock.
    private static void OfferAndLock(Pair pair)
    {
        pair.Rig.Open(pair.Anna.Connection, pair.Bobby.Connection, pair.Anna.Name, pair.Bobby.Name);
        pair.Rig.Offer(pair.Anna.Connection, RowOf(pair.Server, pair.Anna.Connection, Gel), 4);
        pair.Rig.Offer(pair.Anna.Connection, 0, 100);
        pair.Rig.Offer(pair.Bobby.Connection, 0, 20);
        pair.Rig.Lock(pair.Anna.Connection);
        pair.Rig.Lock(pair.Bobby.Connection);
    }

    private static void Confirm(Pair pair)
    {
        pair.Rig.Confirm(pair.Anna.Connection);
        pair.Rig.Confirm(pair.Bobby.Connection);
    }

    private static bool HasHeard(Pair pair, Trader trader, TradeEventKind kind)
    {
        return pair.Rig.Events(trader.Connection).Any(heard => heard.Kind == kind);
    }

    private const string AnnaAfter = Gel + " 6, " + Sword + " 1; 220";
    private const string BobbyAfter = Gel + " 4; 130";

    // A new server on the same database: each enters with what is stored.
    private string BagAfterRestart(Trader trader)
    {
        TestServer restarted = NewServer();
        ConnectionId connection = Enter(restarted, trader.Identity, trader.Character);
        CharacterInventory inventory = CharacterOf(restarted, connection).Inventory;
        return string.Join(
                ", ",
                inventory.Rows.OrderBy(row => row.Item.Value, StringComparer.Ordinal)
                    .Select(row => $"{row.Item.Value} {row.Quantity}"))
            + $"; {inventory.Coins}";
    }

    private long TradesOf(long character)
    {
        return Scalar($"SELECT count(*) FROM trades WHERE {character} IN (first_character_id, second_character_id)");
    }

    private long LedgerRows(long character, string type)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = '{type}'");
    }

    // A bag that cannot take the sword, and coins past the cap, each end the trade naming Bobby, with nothing moved.
    [TestCase(true)]
    [TestCase(false)]
    public void Trade_IntoAFullBag_OrPastTheCoinCap_EndsWithNothingMoved(bool isBagFull)
    {
        Pair pair = EnterPair(
            isBagFull ? "Full" : "Cap",
            prepare: (_, bobby) =>
            {
                if (isBagFull)
                {
                    Execute(
                        "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
                        + $"version) SELECT {bobby}, 'item.weapon.training_sword', 1, 0, 0 FROM "
                        + $"generate_series(1, {PickupSystem.MaxInventoryRows})");
                }
                else
                {
                    Execute($"UPDATE characters SET currency = {ContentLimits.MaxCurrency - 50} WHERE id = {bobby}");
                }
            });
        string bobbyBefore = Bag(pair.Bobby.Character);
        pair.Rig.Open(pair.Anna.Connection, pair.Bobby.Connection, pair.Anna.Name, pair.Bobby.Name);
        pair.Rig.Offer(pair.Anna.Connection, RowOf(pair.Server, pair.Anna.Connection, Sword), 1);
        pair.Rig.Offer(pair.Anna.Connection, 0, 100);
        pair.Rig.Lock(pair.Anna.Connection);
        pair.Rig.Lock(pair.Bobby.Connection);

        Confirm(pair);
        TickUntil(pair.Server, () => pair.Server.Trades.OpenTrades == 0, "ended");

        Assert.That(
            pair.Rig.Events(pair.Anna.Connection).Last(),
            Is.EqualTo(
                (TradeEventKind.Failed, pair.Bobby.Name,
                    isBagFull ? CommandRejectionReason.InventoryFull : CommandRejectionReason.CoinCapReached)));
        Assert.That(
            (Bag(pair.Anna.Character), Bag(pair.Bobby.Character), TradesOf(pair.Anna.Character)),
            Is.EqualTo((Gel + " 10, " + Sword + " 1; 300", bobbyBefore, 0L)));
    }

    // A logout, an expulsion, and a replaced connection while the commit is in flight each wait for it: the exchange
    // lands once, and the restart loads it.
    [TestCase("logout")]
    [TestCase("expel")]
    [TestCase("replace")]
    public void Commit_DuringALogoutAnExpulsionOrAReplacedConnection_IsWaitedFor(string interruption)
    {
        Pair pair = EnterPair($"Wait{char.ToUpperInvariant(interruption[0])}{interruption.Substring(1)}");
        CharacterSession anna = CharacterOf(pair.Server, pair.Anna.Connection);
        ConnectionId again = pair.Server.Connect();
        pair.Server.SignIn(again, $"dev:{pair.Anna.Identity}");
        pair.Server.TickUntil(() => pair.Server.SessionOf(again).Characters != null);
        OfferAndLock(pair);
        pair.Server.RunsPersistence = false;
        Confirm(pair);
        pair.Server.Tick();
        switch (interruption)
        {
            case "logout":
                pair.Server.SendLogout(pair.Anna.Connection, pair.Rig.Next(pair.Anna.Connection));
                break;
            case "expel":
                int violations = new AbuseOptions().ViolationThreshold / ViolationScore.Points;
                for (int index = 0; index < violations; index++)
                {
                    pair.Server.Inbound.OnPayload(pair.Anna.Connection, ProtocolChannel.Control,
                        new byte[] { 0xFF, 0x7F, 0x01 });
                }

                break;
            default:
                pair.Server.SendEnterWorld(again, pair.Anna.Character);
                break;
        }

        pair.Server.Tick(3);
        bool isWaiting = anna.Operation?.Kind == InventoryOperationKind.Trade;
        pair.Server.RunsPersistence = true;
        TickUntil(pair.Server, () => anna.Operation == null && pair.Server.Trades.OpenTrades == 0, "settled");
        pair.Server.Tick(TestServer.TickRate);

        Assert.That(isWaiting, Is.True, "the commit was still in flight");
        Assert.That((Bag(pair.Anna.Character), Bag(pair.Bobby.Character)), Is.EqualTo((AnnaAfter, BobbyAfter)));
        Assert.That(TradesOf(pair.Anna.Character), Is.EqualTo(1L));
        Assert.That(BagAfterRestart(pair.Bobby), Is.EqualTo(BobbyAfter));
    }

    // A refine level set by SQL travels with the sword through a trade, a deposit, and a withdrawal, in the messages
    // and in the rows.
    [Test]
    public void ARefineLevelSetBySql_SurvivesATradeADepositAndAWithdrawal()
    {
        Pair pair = EnterPair(
            "Refine",
            prepare: (anna, _) => Execute(
                $"UPDATE inventory_items SET refine_level = 7 WHERE character_id = {anna} AND "
                + $"item_definition_id = '{Sword}'"));
        pair.Rig.Open(pair.Anna.Connection, pair.Bobby.Connection, pair.Anna.Name, pair.Bobby.Name);
        pair.Rig.Offer(pair.Anna.Connection, RowOf(pair.Server, pair.Anna.Connection, Sword), 1);
        pair.Rig.Lock(pair.Anna.Connection);
        pair.Rig.Lock(pair.Bobby.Connection);
        Confirm(pair);
        TickUntil(pair.Server, () => pair.Server.Trades.OpenTrades == 0, "traded");
        InventoryEntry traded = CharacterOf(pair.Server, pair.Bobby.Connection).Inventory.Rows
            .Single(row => row.Item.Value == Sword);

        NpcEntity keeper = pair.Server.NpcOf(Storekeeper);
        pair.Server.Place(pair.Bobby.Connection, keeper.Position.X + 2f, keeper.Position.Z);
        pair.Server.Tick(2);
        pair.Server.Transport.ClearSent();
        pair.Server.SendStorageDeposit(pair.Bobby.Connection, keeper.Id, traded.InventoryItem, 1,
            pair.Rig.Next(pair.Bobby.Connection));
        pair.Server.Tick();
        TickUntil(pair.Server, () => CharacterOf(pair.Server, pair.Bobby.Connection).Operation == null, "stored");
        StorageChanged stored = pair.Server.Transport.ControlSentTo(pair.Bobby.Connection)
            .Where(message => message.Opcode == MessageOpcode.StorageChanged)
            .Select(message => StorageChanged.TryRead(message.Payload, out StorageChanged? read) ? read! : null!)
            .Single();
        pair.Server.SendStorageWithdraw(
            pair.Bobby.Connection,
            keeper.Id,
            stored.Row.StorageItem,
            1,
            pair.Rig.Next(pair.Bobby.Connection));
        pair.Server.Tick();
        TickUntil(pair.Server, () => CharacterOf(pair.Server, pair.Bobby.Connection).Operation == null, "taken back");

        Assert.That(traded.RefineLevel, Is.EqualTo((byte)7), "after the trade");
        Assert.That(stored.Row.RefineLevel, Is.EqualTo((byte)7), "in storage");
        Assert.That(
            CharacterOf(pair.Server, pair.Bobby.Connection).Inventory.Rows.Single(row => row.Item.Value == Sword)
                .RefineLevel,
            Is.EqualTo((byte)7),
            "taken back");
        Assert.That(
            Scalar(
                $"SELECT refine_level FROM inventory_items WHERE character_id = {pair.Bobby.Character} "
                + $"AND item_definition_id = '{Sword}'"),
            Is.EqualTo(7L));
    }

    // A deposit whose answers are lost is settled from the ledger once: the gel is stored once and the fee taken once,
    // and a withdrawal by the account's other character, and a restart, find it.
    [Test]
    public void Deposit_WhoseAnswersAreLost_IsSettledOnce_AndAnotherCharacterOfTheAccountTakesItBack()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store);
        long anna = Create(server, "v14-store", "AnnaStore");
        long cora = Create(server, "v14-store", "CoraStore");
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({anna}, '{Gel}', 10, 0, 0)");
        Execute($"UPDATE characters SET currency = 300 WHERE id = {anna}");
        ConnectionId annaConnection = Enter(server, "v14-store", anna);
        NpcEntity keeper = server.NpcOf(Storekeeper);
        server.Place(annaConnection, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);
        store.IsLosingAnswers = true;

        server.SendStorageDeposit(annaConnection, keeper.Id, RowOf(server, annaConnection, Gel), 6, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, annaConnection).Operation == null, "the deposit settled");
        store.IsLosingAnswers = false;
        long storedRow = Scalar($"SELECT id FROM storage_items WHERE item_definition_id = '{Gel}' AND quantity = 6");
        ConnectionId coraConnection = Enter(server, "v14-store", cora);
        server.Place(coraConnection, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);
        server.SendStorageWithdraw(coraConnection, keeper.Id, storedRow, 2, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, coraConnection).Operation == null, "the withdrawal settled");

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(
            (Bag(anna), Bag(cora), Scalar($"SELECT quantity FROM storage_items WHERE id = {storedRow}")),
            Is.EqualTo((Gel + " 4; 280", Gel + " 2; 0", 4L)));
        Assert.That(
            (LedgerRows(anna, "storage_deposit"), LedgerRows(cora, "storage_withdraw")),
            Is.EqualTo((1L, 1L)));
        Assert.That(
            BagAfterRestart(new Trader("v14-store", "CoraStore", cora, default)),
            Is.EqualTo(Gel + " 2; 0"));
    }

    // The database pauses with the commit queued: it is cut short and held, the traders waiting; once the database
    // answers, the trade's row or its absence settles it, and the exchange happens at most once.
    [Test]
    public void Trade_ThroughAPausedDatabase_IsHeld_ThenSettledAtMostOnce()
    {
        Pair pair = EnterPair("Pause");
        OfferAndLock(pair);
        pair.Server.RunsPersistence = false;
        Confirm(pair);
        pair.Server.Tick();
        Pause();
        pair.Server.RunsPersistence = true;

        pair.Server.Tick(5);
        bool isHeld = CharacterOf(pair.Server, pair.Anna.Connection).Operation?.Kind == InventoryOperationKind.Trade
            && pair.Server.Trades.OpenTrades == 1;
        Resume();
        TickUntil(
            pair.Server,
            () =>
            {
                pair.Server.Persistence.Probe();
                return pair.Server.Trades.OpenTrades == 0;
            },
            "settled after the pause");

        bool isCompleted = HasHeard(pair, pair.Anna, TradeEventKind.Completed);
        Assert.That(isHeld, Is.True, "held through the pause");
        Assert.That(
            (Bag(pair.Anna.Character), Bag(pair.Bobby.Character), TradesOf(pair.Anna.Character)),
            Is.EqualTo(
                isCompleted
                    ? (AnnaAfter, BobbyAfter, 1L)
                    : (Gel + " 10, " + Sword + " 1; 300", "; 50", 0L)));
        Assert.That(
            HasHeard(pair, pair.Bobby, isCompleted ? TradeEventKind.Completed : TradeEventKind.Failed),
            Is.True,
            "both hear the same end");
    }

    // While the database is known down, the confirms are unsaved and the trade stays open; once it answers, the same
    // trade commits.
    [Test]
    public void Trade_WhileTheDatabaseIsKnownDown_IsUnsaved_AndCommitsOnceItAnswers()
    {
        Pair pair = EnterPair("Down");
        OfferAndLock(pair);
        Pause();
        TickUntil(
            pair.Server,
            () =>
            {
                pair.Server.Persistence.Probe();
                return !pair.Server.Persistence.IsAvailable;
            },
            "the outage known");

        Confirm(pair);
        bool isUnsaved = HasHeard(pair, pair.Anna, TradeEventKind.Unsaved)
            && HasHeard(pair, pair.Bobby, TradeEventKind.Unsaved)
            && pair.Server.Trades.OpenTrades == 1;
        Resume();
        TickUntil(
            pair.Server,
            () =>
            {
                pair.Server.Persistence.Probe();
                return pair.Server.Persistence.IsAvailable;
            },
            "the database back");
        Confirm(pair);
        TickUntil(pair.Server, () => HasHeard(pair, pair.Anna, TradeEventKind.Completed), "committed");

        Assert.That(isUnsaved, Is.True, "unsaved, and still open");
        Assert.That((Bag(pair.Anna.Character), Bag(pair.Bobby.Character)), Is.EqualTo((AnnaAfter, BobbyAfter)));
        Assert.That(TradesOf(pair.Anna.Character), Is.EqualTo(1L));
    }

    // Every answer of the commit is lost: the writer replays it, which the store answers from the trade's row, and then
    // the server asks for the trade, which it finds. The exchange happens once, and the restart loads it.
    [Test]
    public void Trade_WhoseAnswersAreLost_IsSettledFromTheTradeOnce_AndTheRestartKeepsIt()
    {
        var store = new LosingAnswersGameStore(NewStore());
        Pair pair = EnterPair("Lost", store);
        OfferAndLock(pair);
        store.IsLosingAnswers = true;

        Confirm(pair);
        TickUntil(
            pair.Server,
            () => HasHeard(pair, pair.Anna, TradeEventKind.Completed) &&
                HasHeard(pair, pair.Bobby, TradeEventKind.Completed),
            "settled from the trade");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That((Bag(pair.Anna.Character), Bag(pair.Bobby.Character)), Is.EqualTo((AnnaAfter, BobbyAfter)));
        Assert.That((TradesOf(pair.Anna.Character), LedgerRows(pair.Anna.Character, "trade")), Is.EqualTo((1L, 3L)));
        Assert.That(LedgerRows(pair.Bobby.Character, "trade"), Is.EqualTo(3L));
        Assert.That(
            (BagAfterRestart(pair.Anna), BagAfterRestart(pair.Bobby)),
            Is.EqualTo((AnnaAfter, BobbyAfter)));
    }
}
}
