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
///     The Milestone 11 art pass's server side end to end (ROADMAP §8): the composed server host on a real PostgreSQL
///     18, real UDP sockets on loopback, and two clients built from the client's production networking and gameplay
///     code. A wearer holding the training sword, unworn, enters beside an observer, which sees it; the wearer equips
///     the sword; both keep what they hold through a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ArtPassAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Adventurer = "job.adventurer";
    private const string TrainingSword = "item.weapon.training_sword";
    private const string WearerIdentity = "art-pass-wearer";
    private const string WearerName = "Wearer1";
    private const string ObserverIdentity = "art-pass-observer";
    private const string ObserverName = "Observer1";

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
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=5" },
            contentRootPath,
            m_database.ConnectionString);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    // The wearer is stored holding the training sword, unworn, before it first enters; both enter the training ground
    // and see each other; the wearer equips the sword; and the server stops.
    private void PlayTheEquip(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var wearer = new SocketClient(content, WearerIdentity, WearerName);
        using var observer = new SocketClient(content, ObserverIdentity, ObserverName);
        wearer.AfterCreate = () => StoreSword(WearerName);
        EnterTogether(port, wearer, observer);

        Assert.That(SwordOf(wearer).Slot, Is.EqualTo(EquipmentSlot.None), "enter: the sword held, not worn");
        AssertSees("enter", observer, wearer);

        EquipTheSword(wearer, observer);

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => wearer.Connection.State == ClientConnectionState.Disconnected
                    && observer.Connection.State == ClientConnectionState.Disconnected,
                wearer,
                observer),
            Is.True,
            "stop: both clients heard the server stop");
        AssertCleanTraffic("equip", wearer, observer);
    }

    // The equip is committed and told to the wearer (Gameplay Systems §11.1).
    private static void EquipTheSword(SocketClient wearer, SocketClient observer)
    {
        const string step = "equip";
        ClientWorld world = wearer.World;
        wearer.Connection.SendEquip(SwordOf(wearer).InventoryItem);
        Assert.That(
            SocketClients.PumpUntil(() => SwordOf(wearer).Slot == EquipmentSlot.Weapon, wearer, observer),
            Is.True,
            $"{step}: the sword worn; {world.LastRejection}");
        AssertSees(step, observer, wearer);
    }

    // After a clean stop and a second server on the same database, the wearer still wears the sword, and the observer
    // sees it.
    private void PlayAfterTheRestart(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var wearer = new SocketClient(content, WearerIdentity, WearerName);
        using var observer = new SocketClient(content, ObserverIdentity, ObserverName);
        EnterTogether(port, wearer, observer);

        Assert.That(SwordOf(wearer).Slot, Is.EqualTo(EquipmentSlot.Weapon), "restart: the sword still worn");
        AssertSees("restart", observer, wearer);
        AssertCleanTraffic("restart", wearer, observer);
    }

    private static InventoryEntry SwordOf(SocketClient client)
    {
        return client.World.Inventory.Rows.Single(row => row.Item.Value == TrainingSword);
    }

    private static void EnterTogether(int port, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            client.Connect(port);
        }

        bool isEntered = SocketClients.PumpUntil(
            () => clients.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            clients);
        string states = string.Join(
            ", ",
            clients.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"enter: both entered ({states})");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        }
    }

    // The viewer has a spawn for the seen character, an Adventurer.
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: in view");
        RemoteEntity remote = viewer.World.Remotes[entity];
        Assert.That((remote.Kind, remote.DefinitionId), Is.EqualTo((EntityKind.Player, Adventurer)),
            $"{step}: its job");
    }

    private void StoreSword(string name)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + "SELECT id, @item, 1, 0, 0 FROM characters WHERE name = @name",
            connection);
        insert.Parameters.AddWithValue("item", TrainingSword);
        insert.Parameters.AddWithValue("name", name);
        Assert.That(insert.ExecuteNonQuery(), Is.EqualTo(1), "the sword stored, unworn");
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
    public void WornWeapon_OverRealSocketsAndPostgres_IsEquippedAndKeptThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using (IHost first = StartHost(root.Path))
        {
            PlayTheEquip(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
