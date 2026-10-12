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
///     cloth armor, and 1,000 coins, enters the training ground beside Bobby, of another account, with 300; each sees the
///     other by name, and Anna's bag shows what was stored. Anna kills a training slime, whose every drop the script
///     makes, picks up its gel, and walks up to the Smith; then she leaves, and Bobby sees her go. After a clean stop a
///     second host on the same database lets both in again, Anna's bag as she left it. Each later line of Milestone 17
///     adds its steps here: the slime's card, its insertion and removal, the bonuses in the sheet, inspection, the board,
///     and the cards a refine returns.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CardAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Smith = "npc.smith";
    private const string SlimeGel = "item.material.slime_gel";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
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
    // drops at its least amount, since network timing decides which draw each roll gets (Coding Standards §10).
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
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
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
            Is.EqualTo(new[] { $"{ClothArmor} x 1", $"{TrainingSword} x 1" }),
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
        return client.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}")
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
            VisitTheSmith(anna, bobby);
            hunted = Bag(anna);
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
            Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(BobbyCoins), "restart: Bobby's coins");
            AssertCleanTraffic("restart", anna, bobby);
            Stop(host, anna, bobby);
        }
    }
}
}
