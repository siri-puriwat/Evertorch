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
///     The Milestone 11 art pass's server side end to end (ROADMAP §8): the composed server host on a real PostgreSQL
///     18, real UDP sockets on loopback, and two clients built from the client's production networking and gameplay
///     code. A wearer at Adventurer job level 10 holding the training sword, unworn, enters beside an observer, which
///     sees it unarmed; the wearer equips the sword, and the observer sees it in its hand, told once and with no new
///     spawn; after a restart the observer's spawn of the wearer names the sword; the wearer becomes an Arcanist, who
///     cannot wield it, and the observer's replacement spawn shows the Arcanist empty-handed.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ArtPassAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Adventurer = "job.adventurer";
    private const string Arcanist = "job.arcanist";
    private const string Guildmaster = "npc.guildmaster";
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
        wearer.AfterCreate = () =>
        {
            BuildSeed.ReadyToChange.Apply(m_database.ConnectionString, WearerName);
            StoreSword(WearerName);
        };
        EnterTogether(port, wearer, observer);

        Assert.That(SwordOf(wearer).Slot, Is.EqualTo(EquipmentSlot.None), "enter: the sword held, not worn");
        AssertSees("enter", observer, wearer, Adventurer, string.Empty);

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

    // The equip is committed and told to the wearer; the observer sees the sword in its hand, told once, with no new
    // spawn (Gameplay Systems §11.1; Network Protocol §9).
    private static void EquipTheSword(SocketClient wearer, SocketClient observer)
    {
        const string step = "equip";
        ClientWorld world = wearer.World;
        var changes = new List<string>();
        var spawns = new List<EntityId>();
        observer.World.RemoteWornWeaponChanged += remote => changes.Add(remote.WornWeapon);
        observer.World.RemoteSpawned += remote => spawns.Add(remote.Entity);
        wearer.Connection.SendEquip(SwordOf(wearer).InventoryItem);
        Assert.That(
            SocketClients.PumpUntil(() => SwordOf(wearer).Slot == EquipmentSlot.Weapon, wearer, observer),
            Is.True,
            $"{step}: the sword worn; {world.LastRejection}");
        AssertSees(step, observer, wearer, Adventurer, TrainingSword);
        Assert.That(changes, Is.EqualTo(new[] { TrainingSword }), $"{step}: the observer told once");
        Assert.That(spawns, Is.Empty, $"{step}: no new spawn");
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
        AssertSees("restart", observer, wearer, Adventurer, TrainingSword);

        BecomeAnArcanist(wearer, observer);
        AssertCleanTraffic("restart", wearer, observer);
    }

    // The Arcanist cannot wield a sword, so the change takes it off; the observer's replacement spawn shows the new
    // body empty-handed (Gameplay Systems §6.1; Network Protocol §9).
    private static void BecomeAnArcanist(SocketClient wearer, SocketClient observer)
    {
        const string step = "change";
        ClientWorld world = wearer.World;
        Assert.That(
            SocketClients.PumpUntil(() => GuildmasterInView(world) != null, wearer, observer),
            Is.True,
            $"{step}: the Guildmaster in view");
        RemoteEntity guildmaster = GuildmasterInView(world)!;
        int windows = wearer.NpcWindows.Count;
        wearer.TalkTo(guildmaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => wearer.NpcWindows.Count > windows, wearer, observer),
            Is.True,
            $"{step}: walked up to it");
        wearer.Connection.SendChangeJob(guildmaster.Entity, new JobDefinitionId(Arcanist));
        Assert.That(
            SocketClients.PumpUntil(() => SwordOf(wearer).Slot == EquipmentSlot.None, wearer, observer),
            Is.True,
            $"{step}: the sword taken off; {world.LastRejection}");
        EntityId entity = world.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(
                () => observer.World.Remotes.TryGetValue(entity, out RemoteEntity? seen)
                    && seen.DefinitionId == Arcanist,
                wearer,
                observer),
            Is.True,
            $"{step}: the observer sees the Arcanist");
        Assert.That(observer.World.Remotes[entity].WornWeapon, Is.Empty, $"{step}: empty-handed");
    }

    private static RemoteEntity? GuildmasterInView(ClientWorld world)
    {
        return world.Remotes.Values.SingleOrDefault(candidate =>
            candidate.Kind == EntityKind.Npc && candidate.DefinitionId == Guildmaster);
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

    // The viewer has a spawn for the seen character, of the job given, with the weapon given in its hand.
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string job, string weapon)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: in view");
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes[entity].WornWeapon == weapon, viewer, seen),
            Is.True,
            $"{step}: '{weapon}' in its hand");
        RemoteEntity remote = viewer.World.Remotes[entity];
        Assert.That((remote.Kind, remote.DefinitionId), Is.EqualTo((EntityKind.Player, job)), $"{step}: its job");
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
    public void WornWeapon_OverRealSocketsAndPostgres_IsSeenByAnObserverAndKeptThroughARestart()
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
