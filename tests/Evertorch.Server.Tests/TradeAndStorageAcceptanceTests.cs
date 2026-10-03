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
///     The Milestone 14 "Trade and storage" exit criterion's server side end to end (ROADMAP §8): the composed server
///     host on a real PostgreSQL 18 and real UDP sockets on loopback, with three clients built from the client's
///     production networking and gameplay code. Anna, stored holding ten Slime Gel, the Iron Sword, and 300 coins, enters
///     the training ground; Bobby, of another account, and Cora, a second character of Anna's account, enter beside her.
///     Each sees the other two by name, Bobby stands within a trade's reach of Anna, and Cora walks up to the
///     Quartermaster. Anna asks Bobby to trade, Bobby accepts, Anna offers four gel, the sword, and 100 coins, both lock
///     and confirm, and the exchange commits once, each hearing its whole inventory and then the completion. Anna then
///     stores her six gel with the Storekeeper for its fee, Cora takes two of them back there, and Bobby finds his own
///     storage empty and cannot take from theirs. The restart's line adds its step here.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TradeAndStorageAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";
    private const string Storekeeper = "npc.storekeeper";
    private const uint DepositFee = 20;
    private const string SlimeGel = "item.material.slime_gel";
    private const string IronSword = "item.weapon.iron_sword";
    private const string AnnaIdentity = "trade-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "trade-bobby";
    private const string BobbyName = "Bobby";
    private const string CoraName = "Cora";
    private const uint AnnaCoins = 300;
    private const float TradeReach = 3f;

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
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=14" },
            contentRootPath,
            m_database.ConnectionString);
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
            StoreItem(AnnaName, SlimeGel, 10);
            StoreItem(AnnaName, IronSword, 1);
            Execute($"UPDATE characters SET currency = {AnnaCoins} WHERE name = '{AnnaName}'");
        };
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
            Is.EqualTo(new[] { $"{SlimeGel} x 10", $"{IronSword} x 1" }),
            $"{step}: Anna's bag as stored");
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(AnnaCoins), $"{step}: Anna's coins");
        Assert.That(cora.World.Inventory.Rows, Is.Empty, $"{step}: Cora, of Anna's account, holds nothing of hers");
        Assert.That(cora.World.Inventory.Coins, Is.Zero, $"{step}: nor her coins");
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

    // Both entered at the spawn point, so Bobby already stands within a trade's reach of Anna (Gameplay Systems §16).
    private static void StandTogether(SocketClient anna, SocketClient bobby)
    {
        const string step = "together";
        WorldPosition annaAt = anna.World.Predictor.Position;
        Assert.That(bobby.DistanceTo(annaAt), Is.LessThanOrEqualTo(TradeReach), $"{step}: Bobby beside Anna");
        Assert.That(
            anna.World.Remotes[bobby.World.LocalEntity].Kind,
            Is.EqualTo(EntityKind.Player),
            $"{step}: Bobby is a player to Anna");
    }

    // Anna asks, Bobby accepts, Anna offers, both lock and confirm, and the exchange commits once (Gameplay Systems §16).
    private static void Trade(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "trade";
        SocketClient[] all = { anna, bobby, cora };
        var heard = new Dictionary<SocketClient, List<string>>();
        foreach (SocketClient client in all)
        {
            var events = new List<string>();
            heard.Add(client, events);
            client.Connection.TradeEventReceived += tradeEvent => events.Add($"{tradeEvent.Kind} {tradeEvent.Name}");
        }

        anna.Connection.SendTradeRequest(BobbyName);
        Assert.That(
            SocketClients.PumpUntil(() => heard[bobby].Contains($"Requested {AnnaName}"), all),
            Is.True,
            $"{step}: Bobby asked");
        bobby.Connection.SendTradeReply(AnnaName, true);
        Assert.That(
            SocketClients.PumpUntil(
                () => heard[anna].Contains($"Opened {BobbyName}") && heard[bobby].Contains($"Opened {AnnaName}"),
                all),
            Is.True,
            $"{step}: opened for both");

        InventoryEntry gel = anna.World.Inventory.Rows.Single(row => row.Item.Value == SlimeGel);
        InventoryEntry sword = anna.World.Inventory.Rows.Single(row => row.Item.Value == IronSword);
        anna.Connection.SendTradeOffer(gel.InventoryItem, 4);
        anna.Connection.SendTradeOffer(sword.InventoryItem, 1);
        anna.Connection.SendTradeOffer(0, 100);
        anna.Connection.SendTradeLock();
        bobby.Connection.SendTradeLock();
        SocketClients.PumpFor(TimeSpan.FromMilliseconds(300), all);
        anna.Connection.SendTradeConfirm();
        bobby.Connection.SendTradeConfirm();
        Assert.That(
            SocketClients.PumpUntil(
                () => heard[anna].Contains($"Completed {BobbyName}") && heard[bobby].Contains($"Completed {AnnaName}"),
                all),
            Is.True,
            $"{step}: completed for both ({string.Join(", ", heard[anna])}; {anna.World.LastRejection})");
        Assert.That(
            SocketClients.PumpUntil(
                () => bobby.World.Inventory.Coins == 100 && anna.World.Inventory.Coins == AnnaCoins - 100, all),
            Is.True,
            $"{step}: the coins moved");
        Assert.That(
            anna.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}"),
            Is.EqualTo(new[] { $"{SlimeGel} x 6" }),
            $"{step}: Anna kept six gel");
        Assert.That(
            bobby.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(new[] { $"{SlimeGel} x 4", $"{IronSword} x 1" }),
            $"{step}: Bobby holds four gel and the sword");
        Assert.That(
            bobby.World.Inventory.Rows.Single(row => row.Item.Value == IronSword).InventoryItem,
            Is.EqualTo(sword.InventoryItem),
            $"{step}: the sword's row kept its ID");
        Assert.That(heard[cora], Is.Empty, $"{step}: Cora heard nothing of it");
    }

    // Cora walks up to the Quartermaster, whose window opens (Gameplay Systems §6.1).
    private static void VisitTheQuartermaster(SocketClient cora, params SocketClient[] others)
    {
        const string step = "town";
        SocketClient[] all = others.Prepend(cora).ToArray();
        ClientWorld world = cora.World;
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Remotes.Values.Any(remote => remote.DefinitionId == Quartermaster),
                all),
            Is.True,
            $"{step}: the Quartermaster in view");
        RemoteEntity quartermaster = world.Remotes.Values.Single(remote => remote.DefinitionId == Quartermaster);
        cora.TalkTo(quartermaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => cora.NpcWindows.Contains(quartermaster.Entity), all),
            Is.True,
            $"{step}: Cora walked up to it, at {world.Predictor.Position}");
    }

    // Walks the client up to the Storekeeper through the client's own talk, whose window then opens (Gameplay Systems
    // §6.1).
    private static EntityId WalkUpToTheStorekeeper(string step, SocketClient client, SocketClient[] all)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Remotes.Values.Any(remote => remote.DefinitionId == Storekeeper), all),
            Is.True,
            $"{step}: the Storekeeper in view");
        EntityId storekeeper = world.Remotes.Values.Single(remote => remote.DefinitionId == Storekeeper).Entity;
        client.TalkTo(storekeeper);
        Assert.That(
            SocketClients.PumpUntil(() => client.NpcWindows.Contains(storekeeper), all),
            Is.True,
            $"{step}: walked up to it, at {world.Predictor.Position}");
        return storekeeper;
    }

    // Anna stores her six gel for the fee, Cora, of her account, reads them and takes two back, and Bobby, of another
    // account, reads his own empty storage and cannot take from theirs; each hears only its own (Gameplay Systems
    // §11.4).
    private static void Store(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "storage";
        SocketClient[] all = { anna, bobby, cora };
        var parts = new Dictionary<SocketClient, List<StorageSnapshot>>();
        var changes = new Dictionary<SocketClient, List<StorageChanged>>();
        foreach (SocketClient client in all)
        {
            var heard = new List<StorageSnapshot>();
            var changed = new List<StorageChanged>();
            parts.Add(client, heard);
            changes.Add(client, changed);
            client.Connection.StorageSnapshotReceived += heard.Add;
            client.Connection.StorageChangedReceived += changed.Add;
        }

        EntityId storekeeper = WalkUpToTheStorekeeper(step, anna, all);
        anna.Connection.SendStorageOpen(storekeeper);
        Assert.That(SocketClients.PumpUntil(() => parts[anna].Count == 1, all), Is.True, $"{step}: Anna read it");
        Assert.That(
            (parts[anna][0].Revision, parts[anna][0].DepositFee, parts[anna][0].Entries.Count),
            Is.EqualTo((0u, DepositFee, 0)),
            $"{step}: empty, and the fee told");
        InventoryEntry gel = anna.World.Inventory.Rows.Single(row => row.Item.Value == SlimeGel);
        anna.Connection.SendStorageDeposit(storekeeper, gel.InventoryItem, 6);
        Assert.That(
            SocketClients.PumpUntil(
                () => changes[anna].Count == 1 && anna.World.Inventory.Coins == AnnaCoins - 100 - DepositFee,
                all),
            Is.True,
            $"{step}: Anna stored her gel ({anna.World.LastRejection})");
        StorageEntry stored = changes[anna][0].Row;
        Assert.That(
            (changes[anna][0].NewRevision, stored.Item.Value, stored.Quantity),
            Is.EqualTo((1u, SlimeGel, 6u)),
            $"{step}: six gel in storage");
        Assert.That(anna.World.Inventory.Rows, Is.Empty, $"{step}: Anna's bag is empty");

        WalkUpToTheStorekeeper(step, cora, all);
        cora.Connection.SendStorageOpen(storekeeper);
        Assert.That(SocketClients.PumpUntil(() => parts[cora].Count == 1, all), Is.True, $"{step}: Cora read it");
        Assert.That(
            parts[cora][0].Entries.Select(entry => (entry.StorageItem, entry.Item.Value, entry.Quantity)),
            Is.EqualTo(new[] { (stored.StorageItem, SlimeGel, 6u) }),
            $"{step}: Cora sees Anna's gel");
        cora.Connection.SendStorageWithdraw(storekeeper, stored.StorageItem, 2);
        Assert.That(
            SocketClients.PumpUntil(() => changes[cora].Count == 1, all),
            Is.True,
            $"{step}: Cora took two ({cora.World.LastRejection})");
        Assert.That(
            (changes[cora][0].PriorRevision, changes[cora][0].NewRevision, changes[cora][0].Row.Quantity),
            Is.EqualTo((1u, 2u, 4u)),
            $"{step}: four left");
        Assert.That(
            SocketClients.PumpUntil(
                () => cora.World.Inventory.Rows.Any(row => row.Item.Value == SlimeGel && row.Quantity == 2),
                all),
            Is.True,
            $"{step}: Cora holds two gel");

        WalkUpToTheStorekeeper(step, bobby, all);
        bobby.Connection.SendStorageOpen(storekeeper);
        Assert.That(SocketClients.PumpUntil(() => parts[bobby].Count == 1, all), Is.True, $"{step}: Bobby read his");
        Assert.That(parts[bobby][0].Entries, Is.Empty, $"{step}: Bobby's own storage is empty");
        bobby.Connection.SendStorageWithdraw(storekeeper, stored.StorageItem, 1);
        Assert.That(
            SocketClients.PumpUntil(() => bobby.World.LastRejection == CommandRejectionReason.InvalidTarget, all),
            Is.True,
            $"{step}: Bobby cannot take Anna's gel");
        Assert.That(changes[bobby], Is.Empty, $"{step}: Bobby heard no change");
        Assert.That(changes[anna], Has.Count.EqualTo(1), $"{step}: Anna heard only her own deposit");
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

    [Test]
    public void ThreePlayers_TwoOfOneAccount_EnterTogether_AndFindTheirPlaces()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using IHost host = StartHost(root.Path);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
        using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
        using var cora = new SocketClient(content, AnnaIdentity, CoraName);
        Enter(port, anna, bobby, cora);

        AssertSees("enter", anna, bobby, BobbyName, cora);
        AssertSees("enter", bobby, anna, AnnaName, cora);
        AssertSees("enter", anna, cora, CoraName, bobby);
        AssertCleanTraffic("enter", anna, bobby, cora);

        StandTogether(anna, bobby);
        VisitTheQuartermaster(cora, anna, bobby);
        AssertCleanTraffic("town", anna, bobby, cora);

        Trade(anna, bobby, cora);
        AssertCleanTraffic("trade", anna, bobby, cora);

        Store(anna, bobby, cora);
        AssertCleanTraffic("storage", anna, bobby, cora);

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => new[] { anna, bobby, cora }.All(client =>
                    client.Connection.State == ClientConnectionState.Disconnected),
                TimeSpan.FromSeconds(10),
                anna,
                bobby,
                cora),
            Is.True,
            "stop: every client told");
    }
}
}
