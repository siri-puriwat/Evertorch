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
///     The Milestone 17 "Cards and slots" exit criterion's server side end to end (ROADMAP §8): the composed server host
///     on a real PostgreSQL 18 and real UDP sockets on loopback, with scripted combat and drops, and two clients of two
///     accounts built from the client's production networking and gameplay code. Anna, stored holding a training sword,
///     cloth armor, a Forest Crawler Card, a +7 training staff carrying a Spark Wisp Card, an Emberstone, and 1,000
///     coins, enters the training ground beside Bobby, of another account, with 300; each sees the other by name, and
///     Anna's bag shows what was stored. Anna kills a training slime, whose every drop the script makes, its card too,
///     picks up its gel, inserts her card into the sword and wears it, which Bobby inspects and she does not hear, takes
///     it off, and walks up to the Smith; there she takes the card out again for its fee, and refines the staff, which
///     the scripted draw destroys, and its card comes back to her bag. At the Broker she lists the Forest Crawler Card,
///     which Bobby finds on the board. Then she leaves, and Bobby sees her go. After a clean stop a second host on the
///     same database lets both in again, Anna's bag and coins as she left them.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CardAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Smith = "npc.smith";
    private const string Broker = "npc.broker";
    private const string SlimeGel = "item.material.slime_gel";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const string Keen = "item.card.forest_crawler";
    private const string Arcane = "item.card.spark_wisp";
    private const string TrainingStaff = "item.weapon.training_staff";
    private const string Emberstone = "item.material.emberstone";
    private const string AnnaIdentity = "card-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "card-bobby";
    private const string BobbyName = "Bobby";
    private const uint AnnaCoins = 1000;
    private const uint BobbyCoins = 300;

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);

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

    // No reconnect grace, so a player who leaves is out of the world at once. Every hit lands and every drop entry
    // drops at its least amount, and every refine past its safe level fails, since network timing decides which draw
    // each roll gets (Coding Standards §10).
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=17",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0), new ScriptedRandom(99));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void Enter(int port, SocketClient anna, SocketClient bobby)
    {
        const string step = "enter";
        anna.AfterCreate = () =>
        {
            StoreItem(AnnaName, TrainingSword);
            StoreItem(AnnaName, ClothArmor);
            StoreItem(AnnaName, Keen);
            StoreItem(AnnaName, Emberstone);
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
                + $"instance_data_json, version) SELECT id, '{TrainingStaff}', 1, 7, "
                + $"'{{\"cards\": [\"{Arcane}\"]}}'::jsonb, 0 FROM characters WHERE name = '{AnnaName}'");
            Execute($"UPDATE characters SET currency = {AnnaCoins} WHERE name = '{AnnaName}'");
        };
        bobby.AfterCreate = () => Execute($"UPDATE characters SET currency = {BobbyCoins} WHERE name = '{BobbyName}'");
        anna.EnterWorld(port);
        bobby.Connect(port);
        SocketClient[] both = { anna, bobby };
        bool isEntered = SocketClients.PumpUntil(
            () => both.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            both);
        string states = string.Join(", ",
            both.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"{step}: both entered ({states})");
        foreach (SocketClient client in both)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), $"{step}: in town");
        }

        Assert.That(
            Bag(anna),
            Is.EqualTo(
                new[]
                {
                    $"{ClothArmor} x 1", $"{Keen} x 1", $"{Emberstone} x 1", $"{TrainingStaff} x 1 [{Arcane}]",
                    $"{TrainingSword} x 1"
                }),
            $"{step}: Anna's bag as stored");
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(AnnaCoins), $"{step}: Anna's coins");
        Assert.That(bobby.World.Inventory.Rows, Is.Empty, $"{step}: Bobby's bag");
        Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(BobbyCoins), $"{step}: Bobby's coins");
    }

    // The viewer has a spawn for the seen character, which names it (Network Protocol §6).
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string name)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: {name} in view");
        Assert.That(viewer.World.Remotes[entity].Name, Is.EqualTo(name), $"{step}: by its name");
    }

    // Tab, then West once the server confirms the target: the client walks into range, the scripted hits kill the
    // slime, and its drops lie on the ground; F then picks up the gel (Gameplay Systems §10, §11).
    private static void HuntASlime(SocketClient anna, SocketClient bobby)
    {
        const string step = "hunt";
        ClientWorld world = anna.World;
        var deaths = new List<EntityDied>();
        var drops = new List<ItemDropped>();
        world.EntityDiedReceived += deaths.Add;
        world.ItemDroppedReceived += drops.Add;

        EntityId slime = anna.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(
            SocketClients.PumpUntil(() => world.Target == slime, anna, bobby),
            Is.True,
            $"{step}: the server confirmed the target");
        anna.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(
                () => deaths.Any(death => death.Entity == slime) && drops.Count > 0,
                FightLimit,
                anna,
                bobby),
            Is.True,
            $"{step}: the slime died and dropped something");
        Assert.That(world.IsLocalDead, Is.False, $"{step}: Anna survived");
        ItemDropped gel = drops.Single(drop => drop.ItemId == SlimeGel);
        Assert.That(gel.Amount, Is.EqualTo(1u), $"{step}: the gel at its least amount");

        Assert.That(
            SocketClients.PumpUntil(() => !anna.AutoAttack.IsActive, anna, bobby),
            Is.True,
            $"{step}: the attack ended");
        anna.Pickup.Pickup(gel.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => Bag(anna).Contains($"{SlimeGel} x 1"), anna, bobby),
            Is.True,
            $"{step}: the gel reached the bag");
        world.EntityDiedReceived -= deaths.Add;
        world.ItemDroppedReceived -= drops.Add;
    }

    // Anna puts her card into the sword's one slot; her bag's change shows it there, and Bobby hears nothing of it
    // (Gameplay Systems §11.7).
    private static void InsertTheCard(SocketClient anna, SocketClient bobby)
    {
        const string step = "insert";
        ClientWorld world = anna.World;
        long card = world.Inventory.Rows.Single(row => row.Item.Value == Keen).InventoryItem;
        long sword = world.Inventory.Rows.Single(row => row.Item.Value == TrainingSword).InventoryItem;
        uint bobbysRevision = bobby.World.Inventory.Revision;

        Assert.That(anna.Connection.SendInsertCard(card, sword), Is.Not.Zero, $"{step}: sent");
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Inventory.Rows.Any(row => row.InventoryItem == sword && row.Cards.Count == 1),
                anna,
                bobby),
            Is.True,
            $"{step}: the sword holds the card ({world.LastRejection})");

        Assert.That(world.Inventory.Rows.Any(row => row.InventoryItem == card), Is.False,
            $"{step}: the card's row went");
        Assert.That(bobby.World.Inventory.Revision, Is.EqualTo(bobbysRevision), $"{step}: Bobby's bag never changed");
    }

    // Anna wears the carded sword; Bobby inspects her and sees it with its card, and she hears nothing of it; then she
    // takes it off again (Gameplay Systems §11.7).
    private static void InspectAnna(SocketClient anna, SocketClient bobby)
    {
        const string step = "inspect";
        ClientWorld world = anna.World;
        long sword = world.Inventory.Rows.Single(row => row.Item.Value == TrainingSword).InventoryItem;
        var seen = new List<InspectedEquipment>();
        var heard = new List<InspectedEquipment>();
        bobby.Connection.InspectedEquipmentReceived += seen.Add;
        anna.Connection.InspectedEquipmentReceived += heard.Add;

        Assert.That(anna.Connection.SendEquip(sword, EquipmentSlot.Weapon), Is.Not.Zero, $"{step}: worn");
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Inventory.Rows.Any(row => row.InventoryItem == sword && row.Slot == EquipmentSlot.Weapon),
                anna,
                bobby),
            Is.True,
            $"{step}: the sword worn ({world.LastRejection})");
        Assert.That(bobby.Connection.SendInspectPlayer(world.LocalEntity), Is.Not.Zero, $"{step}: asked");
        Assert.That(SocketClients.PumpUntil(() => seen.Count == 1, anna, bobby), Is.True, $"{step}: answered");
        Assert.That(anna.Connection.SendUnequip(EquipmentSlot.Weapon), Is.Not.Zero, $"{step}: taken off");
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Inventory.Rows.Any(row => row.InventoryItem == sword && row.Slot == EquipmentSlot.None),
                anna,
                bobby),
            Is.True,
            $"{step}: the sword in the bag again");
        bobby.Connection.InspectedEquipmentReceived -= seen.Add;
        anna.Connection.InspectedEquipmentReceived -= heard.Add;

        InspectedEquipment answer = seen.Single();
        Assert.That(
            (answer.Entity, answer.ItemIn(EquipmentSlot.Weapon).Item.Value,
                answer.ItemIn(EquipmentSlot.Weapon).Cards.Select(card => card.Value).Single()),
            Is.EqualTo((world.LocalEntity, TrainingSword, Keen)),
            $"{step}: the sword and its card");
        Assert.That(answer.ItemIn(EquipmentSlot.Armor).IsEmpty, Is.True, $"{step}: no armor worn");
        Assert.That(heard, Is.Empty, $"{step}: Anna hears nothing");
    }

    // Anna walks up to the Smith, whose window opens (Gameplay Systems §6.1).
    private static void VisitTheSmith(SocketClient anna, SocketClient bobby)
    {
        const string step = "smith";
        ClientWorld world = anna.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Remotes.Values.Any(remote => remote.DefinitionId == Smith), anna,
                bobby),
            Is.True,
            $"{step}: the Smith in view");
        RemoteEntity smith = world.Remotes.Values.Single(remote => remote.DefinitionId == Smith);
        anna.TalkTo(smith.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => anna.NpcWindows.Contains(smith.Entity), anna, bobby),
            Is.True,
            $"{step}: Anna walked up to it, at {world.Predictor.Position}");
    }

    // At the Smith Anna takes her card out of the sword for its fee of 200; her bag's change shows it back in a row of
    // its own, and Bobby hears nothing of it (Gameplay Systems §11.7).
    private static void TakeTheCardOut(SocketClient anna, SocketClient bobby)
    {
        const string step = "remove";
        ClientWorld world = anna.World;
        EntityId smith = world.Remotes.Values.Single(remote => remote.DefinitionId == Smith).Entity;
        long sword = world.Inventory.Rows.Single(row => row.Item.Value == TrainingSword).InventoryItem;
        uint bobbysRevision = bobby.World.Inventory.Revision;

        Assert.That(
            SocketClients.PumpUntil(() => world.CardRemovalFeesOf(smith).Count > 0, anna, bobby),
            Is.True,
            $"{step}: the Smith's fees");
        Assert.That(
            anna.Connection.SendRemoveCard(smith, sword, 1, new ItemDefinitionId(Keen)),
            Is.Not.Zero,
            $"{step}: sent");
        Assert.That(
            SocketClients.PumpUntil(() => world.Inventory.Rows.Any(row => row.Item.Value == Keen), anna, bobby),
            Is.True,
            $"{step}: the card back in the bag ({world.LastRejection})");

        Assert.That(
            world.Inventory.Rows.Single(row => row.InventoryItem == sword).Cards,
            Is.Empty,
            $"{step}: the sword's slot free");
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnaCoins - 200), $"{step}: the fee");
        Assert.That(bobby.World.Inventory.Revision, Is.EqualTo(bobbysRevision), $"{step}: Bobby's bag never changed");
    }

    // Anna refines the +7 staff, which the scripted draw destroys; its card comes back to her bag (Gameplay Systems
    // §11.6, owner decision 13).
    private static void LoseTheStaff(SocketClient anna, SocketClient bobby)
    {
        const string step = "refine";
        ClientWorld world = anna.World;
        EntityId smith = world.Remotes.Values.Single(remote => remote.DefinitionId == Smith).Entity;
        long staff = world.Inventory.Rows.Single(row => row.Item.Value == TrainingStaff).InventoryItem;
        var outcomes = new List<RefineOutcome>();
        world.RefineOutcomeReceived += outcomes.Add;

        Assert.That(anna.Connection.SendRefine(smith, staff, 7), Is.Not.Zero, $"{step}: sent");
        Assert.That(
            SocketClients.PumpUntil(() => outcomes.Count == 1, anna, bobby),
            Is.True,
            $"{step}: the outcome ({world.LastRejection})");
        world.RefineOutcomeReceived -= outcomes.Add;

        Assert.That(outcomes.Single().Outcome, Is.EqualTo(RefineOutcomeKind.Destroyed), $"{step}: destroyed");
        Assert.That(
            world.Inventory.Rows.Select(row => row.Item.Value),
            Has.Member(Arcane).And.No.Member(TrainingStaff).And.No.Member(Emberstone),
            $"{step}: the card back, the staff and the ore gone");
        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnaCoins - 200 - 20), $"{step}: the refine's coins");
    }

    // At the Broker Anna lists her Forest Crawler Card, a good like any other, and Bobby, of another account, finds it
    // on the board (Gameplay Systems §11.5; owner decision 8).
    private static void ListTheCard(SocketClient anna, SocketClient bobby)
    {
        const string step = "board";
        var notices = new List<MarketEvent>();
        var boards = new List<MarketBoard>();
        anna.Connection.MarketEventReceived += notices.Add;
        bobby.Connection.MarketBoardReceived += boards.Add;
        EntityId broker = WalkUpTo(step, anna, bobby);
        WalkUpTo(step, bobby, anna);
        long card = anna.World.Inventory.Rows.Single(row => row.Item.Value == Keen).InventoryItem;

        Assert.That(anna.Connection.SendMarketList(broker, card, 1), Is.Not.Zero, $"{step}: listed");
        Assert.That(
            SocketClients.PumpUntil(() => notices.Count == 1, anna, bobby),
            Is.True,
            $"{step}: the listing told ({anna.World.LastRejection})");
        Assert.That(bobby.Connection.SendMarketOpen(broker), Is.Not.Zero, $"{step}: read");
        Assert.That(
            SocketClients.PumpUntil(() => bobby.World.Market.IsCurrent && boards.Count > 0, anna, bobby),
            Is.True,
            $"{step}: Bobby read the board ({bobby.World.LastRejection})");
        anna.Connection.MarketEventReceived -= notices.Add;
        bobby.Connection.MarketBoardReceived -= boards.Add;

        Assert.That(
            (notices.Single().Kind, notices.Single().Item.Value, notices.Single().Quantity),
            Is.EqualTo((MarketEventKind.Listed, Keen, 1u)),
            $"{step}: Anna told of her listing");
        Assert.That(
            boards.SelectMany(board => board.Entries).Single(entry => entry.Item.Value == Keen).Stock,
            Is.EqualTo(1u),
            $"{step}: the card on the board for Bobby");
        Assert.That(boards, Has.Count.EqualTo(3), $"{step}: thirty-two goods in three messages");
        Assert.That(anna.World.Inventory.Rows.Any(row => row.Item.Value == Keen), Is.False, $"{step}: the card left");
    }

    // Walks the client up to the Broker through the client's own talk, whose window then opens.
    private static EntityId WalkUpTo(string step, SocketClient client, SocketClient other)
    {
        ClientWorld world = client.World;
        Assert.That(
            SocketClients.PumpUntil(() => world.Remotes.Values.Any(remote => remote.DefinitionId == Broker), client,
                other),
            Is.True,
            $"{step}: the Broker in view");
        EntityId broker = world.Remotes.Values.Single(remote => remote.DefinitionId == Broker).Entity;
        client.TalkTo(broker);
        Assert.That(
            SocketClients.PumpUntil(() => client.NpcWindows.Contains(broker), client, other),
            Is.True,
            $"{step}: walked up to the Broker, at {world.Predictor.Position}");
        return broker;
    }

    // Anna leaves; with no grace her character leaves the world at once, and Bobby sees her go.
    private static void Leave(SocketClient anna, SocketClient bobby)
    {
        const string step = "away";
        EntityId gone = anna.World.LocalEntity;
        anna.Disconnect();
        Assert.That(
            SocketClients.PumpUntil(() => !bobby.World.Remotes.ContainsKey(gone), bobby),
            Is.True,
            $"{step}: Bobby saw Anna go");
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

    private static string[] Bag(SocketClient client)
    {
        return client.World.Inventory.Rows
            .Select(row => $"{row.Item.Value} x {row.Quantity}"
                + (row.Cards.Count == 0
                    ? string.Empty
                    : $" [{string.Join(", ", row.Cards.Select(card => card.Value))}]"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    private void StoreItem(string name, string item)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + "SELECT id, @item, 1, 0, 0 FROM characters WHERE name = @name",
            connection);
        insert.Parameters.AddWithValue("item", item);
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
    public void TwoPlayers_TwoAccounts_HuntInTown_AndKeepTheirBagsThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        ServerContent content;
        string[] hunted;
        uint coins;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            Enter(port, anna, bobby);

            AssertSees("enter", anna, bobby, BobbyName);
            AssertSees("enter", bobby, anna, AnnaName);
            AssertCleanTraffic("enter", anna, bobby);

            HuntASlime(anna, bobby);
            AssertCleanTraffic("hunt", anna, bobby);
            InsertTheCard(anna, bobby);
            AssertCleanTraffic("insert", anna, bobby);
            InspectAnna(anna, bobby);
            AssertCleanTraffic("inspect", anna, bobby);
            VisitTheSmith(anna, bobby);
            TakeTheCardOut(anna, bobby);
            AssertCleanTraffic("remove", anna, bobby);
            LoseTheStaff(anna, bobby);
            AssertCleanTraffic("refine", anna, bobby);
            ListTheCard(anna, bobby);
            AssertCleanTraffic("board", anna, bobby);
            hunted = Bag(anna);
            coins = anna.World.Inventory.Coins;
            Leave(anna, bobby);
            AssertCleanTraffic("away", anna, bobby);
            Stop(host, bobby);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            anna.EnterWorld(port);
            bobby.EnterWorld(port);
            Assert.That(Bag(anna), Is.EqualTo(hunted), "restart: Anna's bag as she left it");
            Assert.That(
                hunted,
                Is.SupersetOf(new[] { $"{TrainingSword} x 1", $"{Arcane} x 1" }).And.No.Member($"{Keen} x 1"),
                "restart: the staff's card back in the bag, the sword's slot free, Keen on the board");
            Assert.That(anna.World.Inventory.Coins, Is.EqualTo(coins), "restart: Anna's coins");
            Assert.That(coins, Is.LessThan(AnnaCoins - 200 - 20), "restart: the listing's fee paid");
            Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(BobbyCoins), "restart: Bobby's coins");
            AssertCleanTraffic("restart", anna, bobby);
            Stop(host, anna, bobby);
        }
    }
}
}
