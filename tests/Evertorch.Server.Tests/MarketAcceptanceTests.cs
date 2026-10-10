using System;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
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
///     Quartermaster. Anna then leaves, and the others see her go; after a clean stop a second host on the same database
///     lets all three in again with their bags and coins as stored. Each later line of Milestone 15 adds its steps here:
///     listing at the Broker, buying while the seller is away, prices that fall and rise with the clock, and collecting.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class MarketAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";
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

    private static readonly DateTimeOffset Start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

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

        AssertBags(step, anna, bobby, cora);
    }

    private static void AssertBags(string step, SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        Assert.That(
            anna.World.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(new[] { $"{GloomDust} x 10", $"{IronSword} x 1" }),
            $"{step}: Anna's bag as stored");
        Assert.That(anna.World.Inventory.Coins, Is.EqualTo(AnnaCoins), $"{step}: Anna's coins");
        Assert.That(bobby.World.Inventory.Rows, Is.Empty, $"{step}: Bobby's bag");
        Assert.That(bobby.World.Inventory.Coins, Is.EqualTo(BobbyCoins), $"{step}: Bobby's coins");
        Assert.That(cora.World.Inventory.Rows, Is.Empty, $"{step}: Cora, of Anna's account, holds nothing of hers");
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

    // Anna walks up to the Quartermaster, whose window opens (Gameplay Systems §6.1).
    private static void VisitTheQuartermaster(SocketClient anna, params SocketClient[] others)
    {
        const string step = "town";
        SocketClient[] all = others.Prepend(anna).ToArray();
        ClientWorld world = anna.World;
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Remotes.Values.Any(remote => remote.DefinitionId == Quartermaster),
                all),
            Is.True,
            $"{step}: the Quartermaster in view");
        RemoteEntity quartermaster = world.Remotes.Values.Single(remote => remote.DefinitionId == Quartermaster);
        anna.TalkTo(quartermaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => anna.NpcWindows.Contains(quartermaster.Entity), all),
            Is.True,
            $"{step}: Anna walked up to it, at {world.Predictor.Position}");
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

    [Test]
    public void ThreePlayers_TwoAccounts_MeetInTown_AndFindTheirBagsAfterARestart()
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
            Enter(port, anna, bobby, cora);

            AssertSees("enter", anna, bobby, BobbyName, cora);
            AssertSees("enter", bobby, anna, AnnaName, cora);
            AssertSees("enter", anna, cora, CoraName, bobby);
            AssertCleanTraffic("enter", anna, bobby, cora);

            VisitTheQuartermaster(anna, bobby, cora);
            Leave(anna, bobby, cora);
            AssertCleanTraffic("away", anna, bobby, cora);
            Stop(host, bobby, cora);
        }

        using (IHost host = StartHost(root.Path))
        {
            int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
            using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
            using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
            using var cora = new SocketClient(content, AnnaIdentity, CoraName);
            anna.EnterWorld(port);
            bobby.EnterWorld(port);
            cora.EnterWorld(port);
            AssertBags("restart", anna, bobby, cora);
            AssertCleanTraffic("restart", anna, bobby, cora);
            Stop(host, anna, bobby, cora);
        }
    }
}
}
