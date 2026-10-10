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
///     The market board on PostgreSQL 18 (Milestone 15 verification; Gameplay Systems §11.5; Persistence §5, §9, §11): a
///     listing, a purchase, and a collection whose answers are lost settled once from the ledger and loaded again after a
///     restart, a purchase held through a paused database and settled at most once, every command refused with 6 while
///     the database is known down, a collection past the coin cap or into a full bag moving nothing, and the coin
///     invariant: all characters' coins, the gross waiting on listings, the fees paid, and the tax collected sum the same
///     before and after the board's commands. Each test lists a good of its own, so no test meets another's price.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MarketDurabilityTests
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

    private const string Dust = "item.material.gloom_dust";
    private const string Carapace = "item.material.grotto_carapace";
    private const string Jelly = "item.material.monarch_jelly";
    private const string Leather = "item.armor.leather";
    private const string Staff = "item.weapon.ash_staff";
    private const string Broker = "npc.broker";
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

    private sealed class Stall
    {
        public Stall(TestServer server, Trader seller, Trader buyer)
        {
            Server = server;
            Seller = seller;
            Buyer = buyer;
        }

        public TestServer Server { get; }

        public Trader Seller { get; }

        public Trader Buyer { get; }

        public EntityId Broker => Server.NpcOf(MarketDurabilityTests.Broker).Id;
    }

    private sealed class Trader
    {
        public Trader(string identity, long character, ConnectionId connection)
        {
            Identity = identity;
            Character = character;
            Connection = connection;
        }

        public string Identity { get; }

        public long Character { get; }

        public ConnectionId Connection { get; }
    }

    private TestServer NewServer(IGameStore? store = null)
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: store ?? NewStore(),
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

    private long LedgerRows(long character, string type)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = '{type}'");
    }

    // What the board's commands never change (Persistence §11): every character's coins, the gross waiting on the
    // listings, the fees paid, and the tax collected.
    private long CoinsAccountedFor()
    {
        return Scalar("SELECT coalesce(sum(currency), 0) FROM characters")
            + Scalar("SELECT coalesce(sum(gross_coins), 0) FROM market_listings")
            - Scalar("SELECT coalesce(sum(currency_delta), 0) FROM economy_ledger WHERE operation_type = 'market_list'")
            + Scalar(
                "SELECT coalesce(sum((metadata_json ->> 'tax')::bigint), 0) FROM economy_ledger "
                + "WHERE operation_type = 'market_collect'");
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

    // Enters and stands two metres west of the Broker, which it knows.
    private static ConnectionId Enter(TestServer server, string identity, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        NpcEntity broker = server.NpcOf(Broker);
        server.Place(connection, broker.Position.X - 2f, broker.Position.Z);
        server.Tick(2);
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

    // A seller stored holding the good, ten of a stack or rows of one, and 100 coins, and a buyer of another account
    // with the coins given, enter beside the Broker.
    private Stall EnterStall(string tag, string good, IGameStore? store = null, int rows = 1, long buyerCoins = 500)
    {
        TestServer server = NewServer(store);
        long seller = Create(server, $"v15-{tag}-a", $"Seller{tag}");
        long buyer = Create(server, $"v15-{tag}-b", $"Buyer{tag}");
        int quantity = rows == 1 ? 10 : 1;
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {seller}, '{good}', {quantity}, 0, 0 FROM generate_series(1, {rows})");
        Execute($"UPDATE characters SET currency = 100 WHERE id = {seller}");
        Execute($"UPDATE characters SET currency = {buyerCoins} WHERE id = {buyer}");
        return new Stall(
            server,
            new Trader($"v15-{tag}-a", seller, Enter(server, $"v15-{tag}-a", seller)),
            new Trader($"v15-{tag}-b", buyer, Enter(server, $"v15-{tag}-b", buyer)));
    }

    private static void Settle(Stall stall, Trader trader, string what)
    {
        stall.Server.Tick();
        TickUntil(stall.Server, () => CharacterOf(stall.Server, trader.Connection).Operation == null, what);
    }

    private static void List(Stall stall, string good, uint quantity, uint sequence)
    {
        stall.Server.SendMarketList(
            stall.Seller.Connection,
            stall.Broker,
            RowOf(stall.Server, stall.Seller.Connection, good),
            quantity,
            sequence);
        Settle(stall, stall.Seller, "listed");
    }

    private long ListingOf(long seller)
    {
        return Scalar($"SELECT id FROM market_listings WHERE seller_character_id = {seller} ORDER BY id LIMIT 1");
    }

    private static CommandRejectionReason LastRefusal(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
    }

    // A new server on the same database: the trader enters with what is stored.
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

    // A collection whose net would pass the coin cap, or whose units find no room in the bag, is refused whole: the
    // listing keeps its units and its sales, and the bag and the coins stay as they were.
    [TestCase(true)]
    [TestCase(false)]
    public void Collection_PastTheCoinCap_OrIntoAFullBag_MovesNothing(bool isCapped)
    {
        Stall stall = EnterStall(isCapped ? "Cap" : "Full", Leather, rows: 2);
        List(stall, Leather, 1, 1);
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Leather, 1, 90, 1);
        Settle(stall, stall.Buyer, "bought");
        long listing = ListingOf(stall.Seller.Character);
        if (isCapped)
        {
            Execute(
                $"UPDATE characters SET currency = {ContentLimits.MaxCurrency - 50} WHERE id = {stall.Seller.Character}");
        }
        else
        {
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT {stall.Seller.Character}, 'item.weapon.training_sword', 1, 0, 0 FROM "
                + $"generate_series(1, {PickupSystem.MaxInventoryRows})");
            Execute($"UPDATE market_listings SET units_left = 1, gross_coins = 0 WHERE id = {listing}");
        }

        string before = Bag(stall.Seller.Character);
        string listed = Scalar($"SELECT units_left FROM market_listings WHERE id = {listing}")
            + " " + Scalar($"SELECT gross_coins FROM market_listings WHERE id = {listing}");

        stall.Server.SendMarketCollect(stall.Seller.Connection, stall.Broker, listing, 2);
        Settle(stall, stall.Seller, "refused");

        Assert.That(
            LastRefusal(stall.Server, stall.Seller.Connection),
            Is.EqualTo(isCapped ? CommandRejectionReason.CoinCapReached : CommandRejectionReason.InventoryFull));
        Assert.That(Bag(stall.Seller.Character), Is.EqualTo(before), "nothing moved");
        Assert.That(
            Scalar($"SELECT units_left FROM market_listings WHERE id = {listing}")
            + " " + Scalar($"SELECT gross_coins FROM market_listings WHERE id = {listing}"),
            Is.EqualTo(listed),
            "the listing kept");
        Assert.That(LedgerRows(stall.Seller.Character, "market_collect"), Is.Zero);
    }

    // While the database is known down, a read and each of the three commands are refused with 6, and nothing moves.
    [Test]
    public void Commands_WhileTheDatabaseIsKnownDown_AreRefusedWithSix()
    {
        Stall stall = EnterStall("Down", Staff, rows: 2);
        List(stall, Staff, 1, 1);
        long listing = ListingOf(stall.Seller.Character);
        string before = Bag(stall.Seller.Character);
        Pause();
        TickUntil(
            stall.Server,
            () =>
            {
                stall.Server.Persistence.Probe();
                return !stall.Server.Persistence.IsAvailable;
            },
            "the outage known");
        ConnectionId seller = stall.Seller.Connection;

        stall.Server.SendMarketOpen(seller, stall.Broker, 2);
        stall.Server.Tick();
        CommandRejectionReason open = LastRefusal(stall.Server, seller);
        stall.Server.SendMarketList(seller, stall.Broker, RowOf(stall.Server, seller, Staff), 1, 3);
        stall.Server.Tick();
        CommandRejectionReason list = LastRefusal(stall.Server, seller);
        stall.Server.SendMarketCollect(seller, stall.Broker, listing, 4);
        stall.Server.Tick();
        CommandRejectionReason collect = LastRefusal(stall.Server, seller);
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Staff, 1, 120, 1);
        stall.Server.Tick();
        CommandRejectionReason buy = LastRefusal(stall.Server, stall.Buyer.Connection);
        Resume();

        Assert.That(
            (open, list, collect, buy),
            Is.EqualTo(
                (CommandRejectionReason.ServiceUnavailable, CommandRejectionReason.ServiceUnavailable,
                    CommandRejectionReason.ServiceUnavailable, CommandRejectionReason.ServiceUnavailable)));
        Assert.That(Bag(stall.Seller.Character), Is.EqualTo(before), "nothing moved");
    }

    // Every answer of the listing, the purchase, and the collection is lost: the writer replays each, which the store
    // answers from the ledger, and then the server asks the ledger, which settles it. Each happens once: one ledger row
    // each, one sale, the fee and the price paid once, the tax taken once, and the restart loads what they left.
    [Test]
    public void ListingPurchaseAndCollection_WhoseAnswersAreLost_HappenOnce_AndTheRestartKeepsThem()
    {
        var store = new LosingAnswersGameStore(NewStore());
        Stall stall = EnterStall("Lost", Dust, store);
        store.IsLosingAnswers = true;

        List(stall, Dust, 10, 1);
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Dust, 4, 28, 1);
        Settle(stall, stall.Buyer, "bought");
        stall.Server.SendMarketCollect(stall.Seller.Connection, stall.Broker, ListingOf(stall.Seller.Character), 2);
        Settle(stall, stall.Seller, "collected");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThanOrEqualTo(3), "answers were lost");
        Assert.That(
            (Bag(stall.Seller.Character), Bag(stall.Buyer.Character)),
            Is.EqualTo(($"{Dust} 6; 200", $"{Dust} 4; 388")),
            "a fee of 2, 112 paid, and a tax of 10");
        Assert.That(
            (LedgerRows(stall.Seller.Character, "market_list"), LedgerRows(stall.Buyer.Character, "market_buy"),
                LedgerRows(stall.Seller.Character, "market_collect")),
            Is.EqualTo((1L, 1L, 1L)));
        Assert.That(
            Scalar($"SELECT count(*) FROM market_sales WHERE buyer_character_id = {stall.Buyer.Character}"),
            Is.EqualTo(1L));
        Assert.That(
            (CharacterOf(stall.Server, stall.Seller.Connection).Inventory.Coins,
                CharacterOf(stall.Server, stall.Buyer.Connection).Inventory.Coins),
            Is.EqualTo((200L, 388L)),
            "the server's copies settled from the ledger");
        Assert.That(
            (BagAfterRestart(stall.Seller), BagAfterRestart(stall.Buyer)),
            Is.EqualTo(($"{Dust} 6; 200", $"{Dust} 4; 388")));
    }

    // The database pauses with the purchase queued: it is cut short and held, the buyer waiting; once the database
    // answers, the ledger settles it, and the purchase happens at most once.
    [Test]
    public void Purchase_ThroughAPausedDatabase_IsHeld_ThenSettledAtMostOnce()
    {
        Stall stall = EnterStall("Pause", Carapace);
        List(stall, Carapace, 10, 1);
        stall.Server.RunsPersistence = false;
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Carapace, 4, 24, 1);
        stall.Server.Tick();
        Pause();
        stall.Server.RunsPersistence = true;

        stall.Server.Tick(5);
        bool isHeld = CharacterOf(stall.Server, stall.Buyer.Connection).Operation?.Kind
            == InventoryOperationKind.MarketBuy;
        Resume();
        TickUntil(
            stall.Server,
            () =>
            {
                stall.Server.Persistence.Probe();
                return CharacterOf(stall.Server, stall.Buyer.Connection).Operation == null;
            },
            "settled after the pause");

        bool isBought = LedgerRows(stall.Buyer.Character, "market_buy") == 1;
        Assert.That(isHeld, Is.True, "held through the pause");
        Assert.That(LedgerRows(stall.Buyer.Character, "market_buy"), Is.LessThanOrEqualTo(1L), "at most once");
        Assert.That(
            Bag(stall.Buyer.Character),
            Is.EqualTo(isBought ? $"{Carapace} 4; 404" : "; 500"),
            "four at 24, or nothing");
        Assert.That(
            CharacterOf(stall.Server, stall.Buyer.Connection).Inventory.Coins,
            Is.EqualTo(isBought ? 404L : 500L),
            "the server's copy agrees");
    }

    // Two sellers list, a buyer of another account takes three units and then every unit left, which raises the price,
    // and both sellers collect: the coins only ever move between characters, listings, the fees, and the tax.
    [Test]
    public void TheBoardsCommands_KeepTheCoinsAccountedFor()
    {
        Stall stall = EnterStall("Coins", Jelly, buyerCoins: 2000);
        long other = Create(stall.Server, "v15-Coins-c", "SellerCoinsTwo");
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({other}, '{Jelly}', 6, 0, 0)");
        Execute($"UPDATE characters SET currency = 50 WHERE id = {other}");
        var second = new Trader("v15-Coins-c", other, Enter(stall.Server, "v15-Coins-c", other));
        long before = CoinsAccountedFor();

        List(stall, Jelly, 10, 1);
        stall.Server.SendMarketList(
            second.Connection,
            stall.Broker,
            RowOf(stall.Server, second.Connection, Jelly),
            6,
            1);
        Settle(stall, second, "the second listed");
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Jelly, 3, 80, 1);
        Settle(stall, stall.Buyer, "three bought");
        stall.Server.SendMarketBuy(stall.Buyer.Connection, stall.Broker, Jelly, 13, 80, 2);
        Settle(stall, stall.Buyer, "the rest bought");
        stall.Server.SendMarketCollect(stall.Seller.Connection, stall.Broker, ListingOf(stall.Seller.Character), 2);
        Settle(stall, stall.Seller, "the first collected");
        stall.Server.SendMarketCollect(second.Connection, stall.Broker, ListingOf(other), 2);
        Settle(stall, second, "the second collected");

        Assert.That(CoinsAccountedFor(), Is.EqualTo(before), "no coin made or lost");
        Assert.That(
            (Bag(stall.Seller.Character), Bag(other), Bag(stall.Buyer.Character)),
            Is.EqualTo(("; 820", "; 483", $"{Jelly} 16; 720")),
            "fees of 8 and 4, 1,280 paid, taxes of 72 and 43");
        Assert.That(
            Scalar($"SELECT price FROM market_prices WHERE item_definition_id = '{Jelly}'"),
            Is.EqualTo(88L),
            "the last units raised the price a step");
    }
}
}
