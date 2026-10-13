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
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 15 "Market board" exit criterion's server side end to end (ROADMAP §8): the composed server host on
///     a real PostgreSQL 18 and real UDP sockets on loopback, its clock a fake one the test moves, with three clients
///     built from the client's production networking and gameplay code. Anna, stored holding ten Gloom Dust, the Iron
///     Sword, and 300 coins, enters the training ground; Bobby, of another account, with 500 coins, and Cora, a second
///     character of Anna's account, with 200, enter beside her. Each sees the other two by name, and Anna walks up to the
///     Quartermaster and then to the Broker, where she lists her dust and her sword for their fees. Cora finds no stock
///     of her own account to buy. Anna leaves; Bobby buys four dust while she is away, a day later buys the six left at
///     the price that fell meanwhile, and the last units raise it a step. After a clean stop a second host on the same
///     database lets all three in again with their bags and coins as stored, and Anna collects her sales less the tax and
///     ends her sword's listing early, taking the sword back.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MarketAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";
    private const string Broker = "npc.broker";
    private const string GloomDust = "item.material.gloom_dust";
    private const string IronSword = "item.weapon.iron_sword";
    private const string AnnaIdentity = "market-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "market-bobby";
    private const string BobbyName = "Bobby";
    private const string CoraName = "Cora";
    private const uint AnnaCoins = 300;
    private const uint BobbyCoins = 500;
    private const uint CoraCoins = 200;

    // The fees of ten dust at 28 and of a sword at 120, 1 % of each rounded down.
    private const uint AnnaListed = AnnaCoins - 2 - 1;

    // Four dust at 28, then six at 26 once a day of listed time made the price fall a step.
    private const uint BobbyBought = BobbyCoins - 4 * 28 - 6 * 26;

    private static readonly DateTimeOffset Start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan PricePeriod = TimeSpan.FromMilliseconds(new MarketOptions().PricePeriodMs);

    private readonly FakeTimeProvider m_clock = new() { UtcNow = Start };
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

    // No reconnect grace, so a seller who leaves is out of the world at once; the market's times come from the test's
    // clock (Gameplay Systems §11.5).
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=15",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        builder.Services.AddSingleton<TimeProvider>(m_clock);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    // Anna enters first, so her account exists before Cora, its second character, signs in with it.
    private void Enter(int port, SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "enter";
        anna.AfterCreate = () =>
        {
            StoreItem(AnnaName, GloomDust, 10);
            StoreItem(AnnaName, IronSword, 1);
            Execute($"UPDATE characters SET currency = {AnnaCoins} WHERE name = '{AnnaName}'");
        };
        bobby.AfterCreate = () => Execute($"UPDATE characters SET currency = {BobbyCoins} WHERE name = '{BobbyName}'");
        cora.AfterCreate = () => Execute($"UPDATE characters SET currency = {CoraCoins} WHERE name = '{CoraName}'");
        anna.EnterWorld(port);
        bobby.Connect(port);
        cora.Connect(port);
        SocketClient[] all = { anna, bobby, cora };
        bool isEntered = SocketClients.PumpUntil(
            () => all.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            all);
        string states = string.Join(", ",
            all.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"{step}: all three entered ({states})");
        foreach (SocketClient client in all)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), $"{step}: in town");
        }

        Assert.That(
            anna.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(new[] { $"{GloomDust} x 10", $"{IronSword} x 1" }),
            $"{step}: Anna's bag as stored");
        AssertCoins(step, anna, bobby, cora, AnnaCoins, BobbyCoins);
        Assert.That(bobby.World.Inventory.Rows, Is.Empty, $"{step}: Bobby's bag");
        Assert.That(cora.World.Inventory.Rows, Is.Empty, $"{step}: Cora, of Anna's account, holds nothing of hers");
    }

    private static void AssertCoins(
        string step,
        SocketClient anna,
        SocketClient bobby,
        SocketClient cora,
        uint annaCoins,
        uint bobbyCoins)
    {
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(annaCoins), $"{step}: Anna's coins");
        Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(bobbyCoins), $"{step}: Bobby's coins");
        Assert.That(cora.World.Inventory.Coins, Is.EqualTo(CoraCoins), $"{step}: Cora's own coins");
    }

    // The viewer has a spawn for the seen character, which names it (Network Protocol §6).
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string name, SocketClient other)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen, other),
            Is.True,
            $"{step}: {name} in view");
        Assert.That(viewer.World.Remotes[entity].Name, Is.EqualTo(name), $"{step}: by its name");
    }

    // Walks the client up to the NPC through the client's own talk, whose window then opens (Gameplay Systems §6.1).
    private static EntityId WalkUpTo(string step, string npc, SocketClient client, SocketClient[] all)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Remotes.Values.Any(remote => remote.DefinitionId == npc), all),
            Is.True,
            $"{step}: {npc} in view");
        EntityId entity = world.Remotes.Values.Single(remote => remote.DefinitionId == npc).Entity;
        client.TalkTo(entity);
        Assert.That(
            SocketClients.PumpUntil(() => client.NpcWindows.Contains(entity), all),
            Is.True,
            $"{step}: walked up to {npc}, at {world.Predictor.Position}");
        return entity;
    }

    // What each client heard of the board: its entries, its own listings, and the notices of its listings.
    private static Dictionary<SocketClient, Heard> Listen(params SocketClient[] clients)
    {
        var heard = new Dictionary<SocketClient, Heard>();
        foreach (SocketClient client in clients)
        {
            var each = new Heard();
            client.Connection.MarketBoardReceived += board =>
            {
                each.Boards.Add(board);
                each.Order.Add('b');
            };
            client.Connection.MarketMineReceived += mine =>
            {
                each.Mines.Add(mine);
                each.Order.Add('m');
            };
            client.Connection.MarketEventReceived += each.Notices.Add;
            heard.Add(client, each);
        }

        return heard;
    }

    // Opens the board at the Broker and waits for the read's entries and then the listings that end it; listings that
    // end the push of an earlier command may still be on their way, and do not count.
    private static void Read(string step, SocketClient client, EntityId broker, Heard heard, SocketClient[] all)
    {
        int start = heard.Order.Count;
        client.Connection.SendMarketOpen(broker);
        Assert.That(
            SocketClients.PumpUntil(
                () =>
                {
                    int board = heard.Order.IndexOf('b', start);
                    return board >= 0 && heard.Order.IndexOf('m', board) >= 0;
                },
                all),
            Is.True,
            $"{step}: read the board ({client.World.LastRejection})");
    }

    // Anna opens the board, every listable item at its base and none for her to buy, then lists her ten dust and her
    // sword; each listing is told and both are hers to see (Gameplay Systems §11.5).
    private static void List(SocketClient anna, Heard heard, SocketClient[] all)
    {
        const string step = "list";
        EntityId broker = WalkUpTo(step, Broker, anna, all);
        Read(step, anna, broker, heard, all);
        Assert.That(
            heard.Boards.SelectMany(board => board.Entries)
                .Select(entry => (entry.Item.Value, entry.Price, entry.Stock)),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.leather", 90u, 0u), ("item.armor.monarch_mantle", 1000u, 0u),
                    ("item.card.forest_crawler", 800u, 0u), ("item.card.gloom_wisp", 2400u, 0u),
                    ("item.card.grotto_crawler", 2400u, 0u), ("item.card.slime_monarch", 24000u, 0u),
                    ("item.card.spark_wisp", 800u, 0u), ("item.card.training_slime", 800u, 0u),
                    ("item.consumable.earth_converter", 60u, 0u), ("item.consumable.fire_converter", 60u, 0u),
                    ("item.consumable.water_converter", 60u, 0u), ("item.consumable.wind_converter", 60u, 0u),
                    ("item.material.emberstone", 40u, 0u), (GloomDust, 28u, 0u),
                    ("item.material.grotto_carapace", 24u, 0u), ("item.material.monarch_jelly", 80u, 0u),
                    ("item.material.wardstone", 40u, 0u), ("item.weapon.ash_staff", 120u, 0u), (IronSword, 120u, 0u)
                }),
            $"{step}: every listable item at its base");
        Assert.That(heard.Mines.Single().Entries, Is.Empty, $"{step}: nothing listed yet");

        InventoryEntry dust = anna.World.Inventory.Rows.Single(row => row.Item.Value == GloomDust);
        anna.Connection.SendMarketList(broker, dust.InventoryItem, 10);
        Assert.That(
            SocketClients.PumpUntil(() => heard.Notices.Count == 1 && heard.Mines.Count == 2, all),
            Is.True,
            $"{step}: Anna listed her dust ({anna.World.LastRejection})");
        InventoryEntry sword = anna.World.Inventory.Rows.Single(row => row.Item.Value == IronSword);
        anna.Connection.SendMarketList(broker, sword.InventoryItem, 1);
        Assert.That(
            SocketClients.PumpUntil(() => heard.Notices.Count == 2 && heard.Mines.Count == 3, all),
            Is.True,
            $"{step}: Anna listed her sword ({anna.World.LastRejection})");
        Assert.That(
            heard.Notices.Select(notice => (notice.Kind, notice.Item.Value, notice.Quantity, notice.Coins)),
            Is.EqualTo(
                new[] { (MarketEventKind.Listed, GloomDust, 10u, 2u), (MarketEventKind.Listed, IronSword, 1u, 1u) }),
            $"{step}: each listing told, with its fee");
        Assert.That(
            heard.Mines.Last().Entries.Select(entry => (entry.Item.Value, entry.UnitsLeft, entry.SecondsLeft)),
            Is.EqualTo(new[] { (GloomDust, 10u, 259_200u), (IronSword, 1u, 259_200u) }),
            $"{step}: both selling for three days");
        Assert.That(anna.World.Inventory.Rows, Is.Empty, $"{step}: Anna's bag is empty");
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(AnnaListed), $"{step}: the fees paid");
    }

    // Cora, of Anna's account, sees none of Anna's stock and cannot buy it (Gameplay Systems §11.5).
    private static void FindNoStockOfItsOwnAccount(SocketClient cora, Heard heard, SocketClient[] all)
    {
        const string step = "own account";
        EntityId broker = WalkUpTo(step, Broker, cora, all);
        Read(step, cora, broker, heard, all);
        Assert.That(
            heard.Boards.SelectMany(board => board.Entries).Where(entry => entry.Stock > 0),
            Is.Empty,
            $"{step}: no stock of her own account");
        Assert.That(heard.Mines.Single().Entries, Is.Empty, $"{step}: Anna's listings are Anna's");
        cora.Connection.SendMarketBuy(broker, new ItemDefinitionId(GloomDust), 1, 28);
        Assert.That(
            SocketClients.PumpUntil(() => cora.World.LastRejection == CommandRejectionReason.InvalidTarget, all),
            Is.True,
            $"{step}: Cora cannot buy Anna's dust");
        Assert.That(cora.World.Inventory.Coins, Is.EqualTo(CoraCoins), $"{step}: Cora paid nothing");
    }

    // Anna leaves; with no grace her character leaves the world at once, and the others see her go.
    private static void Leave(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "away";
        EntityId gone = anna.World.LocalEntity;
        anna.Disconnect();
        Assert.That(
            SocketClients.PumpUntil(
                () => !bobby.World.Remotes.ContainsKey(gone) && !cora.World.Remotes.ContainsKey(gone),
                bobby,
                cora),
            Is.True,
            $"{step}: Bobby and Cora saw Anna go");
    }

    // Bobby buys four of Anna's dust while she is away: the coins wait on her listing (Gameplay Systems §11.5).
    private static EntityId BuyWhileTheSellerIsAway(SocketClient bobby, Heard heard, SocketClient[] all)
    {
        const string step = "buy";
        EntityId broker = WalkUpTo(step, Broker, bobby, all);
        Read(step, bobby, broker, heard, all);
        Assert.That(EntryOf(heard, GloomDust), Is.EqualTo((28u, 10u)), $"{step}: Anna's ten dust on sale");
        Assert.That(EntryOf(heard, IronSword), Is.EqualTo((120u, 1u)), $"{step}: and her sword");
        bobby.Connection.SendMarketBuy(broker, new ItemDefinitionId(GloomDust), 4, 28);
        Assert.That(
            SocketClients.PumpUntil(
                () => bobby.World.Inventory.Coins == BobbyCoins - 4 * 28 && EntryOf(heard, GloomDust).Stock == 6,
                all),
            Is.True,
            $"{step}: Bobby bought four ({bobby.World.LastRejection})");
        Assert.That(
            bobby.World.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity)),
            Is.EqualTo(new[] { (GloomDust, 4u) }),
            $"{step}: four dust in Bobby's bag");
        Assert.That(heard.Notices, Is.Empty, $"{step}: a purchase's line is the shop's");
        return broker;
    }

    // A day of listed time makes the dust fall a step, 28 to 26; Bobby buys the six left, the last he could take, and
    // they raise it a step again (Gameplay Systems §11.5).
    private void FallAndRise(SocketClient bobby, EntityId broker, Heard heard, SocketClient[] all)
    {
        const string step = "prices";
        m_clock.UtcNow = Start + PricePeriod;
        Read(step, bobby, broker, heard, all);
        Assert.That(EntryOf(heard, GloomDust), Is.EqualTo((26u, 6u)), $"{step}: a day of stock, one fall");
        Assert.That(EntryOf(heard, IronSword), Is.EqualTo((108u, 1u)), $"{step}: the sword's too, a tenth of 120");
        bobby.Connection.SendMarketBuy(broker, new ItemDefinitionId(GloomDust), 6, 26);
        Assert.That(
            SocketClients.PumpUntil(
                () => bobby.World.Inventory.Coins == BobbyBought && EntryOf(heard, GloomDust).Stock == 0,
                all),
            Is.True,
            $"{step}: Bobby bought the six left ({bobby.World.LastRejection})");
        Assert.That(EntryOf(heard, GloomDust), Is.EqualTo((28u, 0u)), $"{step}: the last units raised it a step");
        Assert.That(
            bobby.World.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity)),
            Is.EqualTo(new[] { (GloomDust, 10u) }),
            $"{step}: ten dust in Bobby's bag");
    }

    // Back after the restart, Anna collects her dust's sales less the tax, 268 less 24, and ends her sword's listing
    // early, the fee kept and the sword back (Gameplay Systems §11.5).
    private static void Collect(SocketClient anna, Heard heard, SocketClient[] all)
    {
        const string step = "collect";
        EntityId broker = WalkUpTo(step, Broker, anna, all);
        Read(step, anna, broker, heard, all);
        MarketListingEntry[] listings = heard.Mines.Last().Entries.ToArray();
        Assert.That(
            listings.Select(entry => (entry.Item.Value, entry.UnitsLeft, entry.UnitsSold, entry.CoinsWaiting,
                entry.SecondsLeft)),
            Is.EqualTo(new[] { (GloomDust, 0u, 10u, 244u, 172_800u), (IronSword, 1u, 0u, 0u, 172_800u) }),
            $"{step}: the dust sold out, the sword still selling for two days");
        anna.Connection.SendMarketCollect(broker, listings[0].Listing);
        Assert.That(
            SocketClients.PumpUntil(() => heard.Notices.Count == 1 && heard.Mines.Last().Entries.Count == 1, all),
            Is.True,
            $"{step}: Anna collected her dust's sales ({anna.World.LastRejection})");
        anna.Connection.SendMarketCollect(broker, listings[1].Listing);
        Assert.That(
            SocketClients.PumpUntil(() => heard.Notices.Count == 2 && heard.Mines.Last().Entries.Count == 0, all),
            Is.True,
            $"{step}: Anna ended her sword's listing ({anna.World.LastRejection})");
        Assert.That(
            heard.Notices.Select(notice => (notice.Kind, notice.Item.Value, notice.Quantity, notice.Coins)),
            Is.EqualTo(
                new[]
                {
                    (MarketEventKind.Collected, GloomDust, 0u, 244u), (MarketEventKind.Collected, IronSword, 1u, 0u)
                }),
            $"{step}: each collection told");
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(AnnaListed + 244), $"{step}: the net in Anna's coins");
        Assert.That(
            anna.World.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity)),
            Is.EqualTo(new[] { (IronSword, 1u) }),
            $"{step}: the sword back");
    }

    private static (uint Price, uint Stock) EntryOf(Heard heard, string item)
    {
        MarketBoardEntry entry = heard.Boards.SelectMany(board => board.Entries).Last(each => each.Item.Value == item);
        return (entry.Price, entry.Stock);
    }

    // Anna walks up to the Quartermaster, whose window opens (Gameplay Systems §6.1).
    private static void VisitTheQuartermaster(SocketClient anna, SocketClient[] all)
    {
        WalkUpTo("town", Quartermaster, anna, all);
    }

    private static void Stop(IHost host, params SocketClient[] clients)
    {
        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => clients.All(client => client.Connection.State == ClientConnectionState.Disconnected),
                TimeSpan.FromSeconds(10),
                clients),
            Is.True,
            "stop: every client told");
    }

    private void StoreItem(string name, string item, int quantity)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + "SELECT id, @item, @quantity, 0, 0 FROM characters WHERE name = @name",
            connection);
        insert.Parameters.AddWithValue("item", item);
        insert.Parameters.AddWithValue("quantity", quantity);
        insert.Parameters.AddWithValue("name", name);
        Assert.That(insert.ExecuteNonQuery(), Is.EqualTo(1), $"{item} stored");
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        Assert.That(command.ExecuteNonQuery(), Is.EqualTo(1), sql);
    }

    private static void AssertCleanTraffic(string step, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
            Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
        }
    }

    private sealed class Heard
    {
        public List<MarketBoard> Boards { get; } = new();

        public List<MarketMine> Mines { get; } = new();

        public List<MarketEvent> Notices { get; } = new();

        /// <summary>
        ///     The order the entries ('b') and the listings ('m') arrived in.
        /// </summary>
        public List<char> Order { get; } = new();
    }

    [Test]
    public void ThreePlayers_TwoAccounts_ListBuyWhileTheSellerIsAway_AndCollectAfterARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        ServerContent content;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            using var cora = new SocketClient(content, AnnaIdentity, CoraName);
            SocketClient[] all = { anna, bobby, cora };
            Dictionary<SocketClient, Heard> heard = Listen(all);
            Enter(port, anna, bobby, cora);

            AssertSees("enter", anna, bobby, BobbyName, cora);
            AssertSees("enter", bobby, anna, AnnaName, cora);
            AssertSees("enter", anna, cora, CoraName, bobby);
            AssertCleanTraffic("enter", all);

            VisitTheQuartermaster(anna, all);
            List(anna, heard[anna], all);
            FindNoStockOfItsOwnAccount(cora, heard[cora], all);
            AssertCleanTraffic("list", all);
            Leave(anna, bobby, cora);
            SocketClient[] staying = { bobby, cora };
            EntityId broker = BuyWhileTheSellerIsAway(bobby, heard[bobby], staying);
            FallAndRise(bobby, broker, heard[bobby], staying);
            Assert.That(heard[cora].Notices, Is.Empty, "prices: Cora heard nothing of Anna's sales");
            AssertCleanTraffic("prices", bobby, cora);
            Stop(host, bobby, cora);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            using var cora = new SocketClient(content, AnnaIdentity, CoraName);
            SocketClient[] all = { anna, bobby, cora };
            Dictionary<SocketClient, Heard> heard = Listen(all);
            anna.EnterWorld(port);
            bobby.EnterWorld(port);
            cora.EnterWorld(port);
            AssertCoins("restart", anna, bobby, cora, AnnaListed, BobbyBought);
            Assert.That(anna.World.Inventory.Rows, Is.Empty, "restart: Anna's goods still listed");
            Assert.That(
                bobby.World.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity)),
                Is.EqualTo(new[] { (GloomDust, 10u) }),
                "restart: Bobby's dust as bought");
            Collect(anna, heard[anna], all);
            Assert.That(heard[bobby].Notices.Concat(heard[cora].Notices), Is.Empty, "collect: told Anna alone");
            AssertCleanTraffic("restart", all);
            Stop(host, anna, bobby, cora);
        }
    }
}
}
