using System;
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
///     Quartermaster, beside whom the Storekeeper will stand. Each later line of Milestone 14 adds its steps here: the
///     trade, storage, and a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TradeAndStorageAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Quartermaster = "npc.quartermaster";
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
