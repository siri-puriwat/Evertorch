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
///     The Milestone 16 "Refining" exit criterion's server side end to end (ROADMAP §8): the composed server host on a
///     real PostgreSQL 18 and real UDP sockets on loopback, with a seed, and two clients of two accounts built from the
///     client's production networking and gameplay code. Anna, stored holding a training sword at +3, cloth armor, ten
///     Emberstones, and 500 coins, enters the training ground beside Bobby, of another account, with 300; each sees the
///     other by name, Anna's bag shows the sword's level, and Anna walks up to the Quartermaster. She then walks up to
///     the Smith, whose three tables follow its services, refines the sword to +7, the training table's safe level, one
///     level, 20 coins, and one Emberstone a time, and once past it, where the seed decides; Bobby hears none of it.
///     Anna then leaves, and Bobby sees her go; after a clean stop a second host on the same database lets both in again,
///     Anna's bag as the refines left it. Each later line of Milestone 16 adds its steps here: the level in combat and in
///     the sheet, a refined item through a trade and storage, and the grotto's ores on the board.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RefiningAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";
    private const string Smith = "npc.smith";
    private const string Emberstone = "item.material.emberstone";
    private const int Ores = 10;
    private const int SafeLevel = 7;
    private const uint CoinsALevel = 20;
    private const string TrainingSword = "item.weapon.training_sword";
    private const string ClothArmor = "item.armor.cloth";
    private const string AnnaIdentity = "refine-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "refine-bobby";
    private const string BobbyName = "Bobby";
    private const uint AnnaCoins = 500;
    private const uint BobbyCoins = 300;
    private const int SwordLevel = 3;

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

    // No reconnect grace, so a player who leaves is out of the world at once; the seed fixes every draw, a refine's
    // among them (Gameplay Systems §11.6).
    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[]
            {
                "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=16",
                "--Session:ReconnectGraceMs=0"
            },
            contentRootPath,
            m_database.ConnectionString);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private void Enter(int port, SocketClient anna, SocketClient bobby)
    {
        const string step = "enter";
        anna.AfterCreate = () =>
        {
            StoreItem(AnnaName, TrainingSword, SwordLevel);
            StoreItem(AnnaName, ClothArmor, 0);
            StoreItem(AnnaName, Emberstone, 0, Ores);
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

        AssertBags(step, anna, bobby);
    }

    // The owner sees its row's level (Network Protocol §9).
    private static void AssertBags(string step, SocketClient anna, SocketClient bobby)
    {
        Assert.That(
            anna.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity} +{row.RefineLevel}")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(
                new[] { $"{ClothArmor} x 1 +0", $"{Emberstone} x {Ores} +0", $"{TrainingSword} x 1 +{SwordLevel}" }),
            $"{step}: Anna's bag as stored, the sword at +{SwordLevel}");
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

    // Anna walks up to the Quartermaster, whose window opens (Gameplay Systems §6.1).
    private static void VisitTheQuartermaster(SocketClient anna, SocketClient bobby)
    {
        const string step = "town";
        ClientWorld world = anna.World;
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Remotes.Values.Any(remote => remote.DefinitionId == Quartermaster),
                anna,
                bobby),
            Is.True,
            $"{step}: the Quartermaster in view");
        RemoteEntity quartermaster = world.Remotes.Values.Single(remote => remote.DefinitionId == Quartermaster);
        anna.TalkTo(quartermaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => anna.NpcWindows.Contains(quartermaster.Entity), anna, bobby),
            Is.True,
            $"{step}: Anna walked up to it, at {world.Predictor.Position}");
    }

    private static string[] Bag(SocketClient client)
    {
        return client.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity} +{row.RefineLevel}")
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();
    }

    // Anna walks up to the Smith, whose three tables followed its services when it came into view, and refines the
    // sword one level at a time to the safe level, each a draw below 100, then once past it, where the training table
    // destroys the sword on a failure (Gameplay Systems §11.6). The refiner alone hears each outcome.
    private static void RefineAtTheSmith(
        SocketClient anna,
        SocketClient bobby,
        List<RefineTable> tables,
        List<RefineOutcome> outcomes,
        List<RefineOutcome> overheard)
    {
        const string step = "refine";
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
        Assert.That(
            tables.Where(table => table.Npc == smith.Entity).Select(table => table.Index),
            Is.EqualTo(new byte[] { 0, 1, 2, 3 }),
            $"{step}: its four tables");
        long sword = world.Inventory.Rows.Single(row => row.Item.Value == TrainingSword).InventoryItem;
        for (int level = SwordLevel; level < SafeLevel; level++)
        {
            int heard = outcomes.Count;
            Assert.That(anna.Connection.SendRefine(smith.Entity, sword, (byte)level), Is.Not.Zero, $"{step}: +{level}");
            Assert.That(
                SocketClients.PumpUntil(() => outcomes.Count > heard, anna, bobby),
                Is.True,
                $"{step}: the outcome from +{level}");
            Assert.That(
                (outcomes[^1].Outcome, outcomes[^1].LevelAfter),
                Is.EqualTo((RefineOutcomeKind.Refined, (byte)(level + 1))),
                $"{step}: safe from +{level}");
        }

        Assert.That(world.Inventory.Coins, Is.EqualTo(AnnaCoins - (SafeLevel - SwordLevel) * CoinsALevel));
        int before = outcomes.Count;
        anna.Connection.SendRefine(smith.Entity, sword, SafeLevel);
        Assert.That(SocketClients.PumpUntil(() => outcomes.Count > before, anna, bobby), Is.True, $"{step}: past +7");
        RefineOutcome past = outcomes[^1];
        string[] swords = Bag(anna).Where(row => row.StartsWith(TrainingSword, StringComparison.Ordinal)).ToArray();
        Assert.That(
            past.Outcome == RefineOutcomeKind.Refined
                ? swords.SequenceEqual(new[] { $"{TrainingSword} x 1 +8" })
                : past.Outcome == RefineOutcomeKind.Destroyed && swords.Length == 0,
            Is.True,
            $"{step}: past the safe level, {past.Outcome} and the bag agree");
        Assert.That(
            Bag(anna).Single(row => row.StartsWith(Emberstone, StringComparison.Ordinal)),
            Is.EqualTo($"{Emberstone} x {Ores - (SafeLevel - SwordLevel) - 1} +0"),
            $"{step}: one Emberstone an attempt");
        Assert.That(overheard, Is.Empty, $"{step}: Bobby heard nothing");
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

    private void StoreItem(string name, string item, int refineLevel, int quantity = 1)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + "SELECT id, @item, @quantity, @level, 0 FROM characters WHERE name = @name",
            connection);
        insert.Parameters.AddWithValue("item", item);
        insert.Parameters.AddWithValue("quantity", quantity);
        insert.Parameters.AddWithValue("level", refineLevel);
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
    public void TwoPlayers_TwoAccounts_MeetInTown_AndKeepARefinedSwordThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildSeededPackage());

        ServerContent content;
        string[] refined;
        using (IHost host = StartHost(root.Path))
        {
            content = host.Services.GetRequiredService<ServerContent>();
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            var tables = new List<RefineTable>();
            var outcomes = new List<RefineOutcome>();
            var overheard = new List<RefineOutcome>();
            anna.Connection.RefineTableReceived += tables.Add;
            anna.Connection.RefineOutcomeReceived += outcomes.Add;
            bobby.Connection.RefineOutcomeReceived += overheard.Add;
            Enter(port, anna, bobby);

            AssertSees("enter", anna, bobby, BobbyName);
            AssertSees("enter", bobby, anna, AnnaName);
            AssertCleanTraffic("enter", anna, bobby);

            VisitTheQuartermaster(anna, bobby);
            RefineAtTheSmith(anna, bobby, tables, outcomes, overheard);
            AssertCleanTraffic("refine", anna, bobby);
            refined = Bag(anna);
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
            Assert.That(Bag(anna), Is.EqualTo(refined), "restart: Anna's bag as the refines left it");
            Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(BobbyCoins), "restart: Bobby's coins");
            AssertCleanTraffic("restart", anna, bobby);
            Stop(host, anna, bobby);
        }
    }
}
}
