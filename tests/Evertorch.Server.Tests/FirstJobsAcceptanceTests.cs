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
///     The Milestone 10 first jobs (ROADMAP §8) end to end: the composed server host on a real PostgreSQL 18, real UDP
///     sockets on loopback, and two clients built from the client's production networking and gameplay code, pumped
///     side by side on the test's thread. A changer stored at Adventurer job level 10, with skill points left unspent
///     and the training staff worn, enters beside a second character at the same level, which sees it; both keep what
///     they hold through a restart. Each later line of Milestone 10 adds its steps here: the change to a Vanguard with
///     the staff taken off, a learned Heavy Blow, the second character's view of the Vanguard, and the second character
///     becoming an Arcanist and Mending the first.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, and every entry of a drop table drops its smallest amount.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class FirstJobsAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string Adventurer = "job.adventurer";
    private const string TrainingStaff = "item.weapon.training_staff";
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";
    private const string ChangerIdentity = "first-jobs-changer";
    private const string ChangerName = "Changer1";
    private const string MenderIdentity = "first-jobs-mender";
    private const string MenderName = "Mender1";

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
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    /// <summary>
    ///     Both characters are stored at Adventurer job level 10 before they first enter, the changer wearing the
    ///     training staff; they enter the training ground and see each other, and the server stops.
    /// </summary>
    private void PlayBeforeTheChange(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var changer = new SocketClient(content, ChangerIdentity, ChangerName);
        using var mender = new SocketClient(content, MenderIdentity, MenderName);
        changer.AfterCreate = () =>
        {
            BuildSeed.ReadyToChange.Apply(m_database.ConnectionString, ChangerName);
            StoreWornStaff(ChangerName);
        };
        mender.AfterCreate = () => BuildSeed.ReadyToChange.Apply(m_database.ConnectionString, MenderName);
        EnterTogether(port, changer, mender);

        AssertReadyToChange("enter", changer);
        AssertWearsTheStaff("enter", changer);
        AssertReadyToChange("enter", mender);
        AssertSees("enter", mender, changer, Adventurer);
        Assert.That(
            admin.GetPlayers(AdminActor.LocalConsole).Select(player => player.JobLevel),
            Is.All.EqualTo(10),
            "enter: the console shows both at job level 10");

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => changer.Connection.State == ClientConnectionState.Disconnected
                    && mender.Connection.State == ClientConnectionState.Disconnected,
                changer,
                mender),
            Is.True,
            "stop: both clients heard the server stop");
        AssertCleanTraffic("before the change", changer, mender);
    }

    // After a clean stop and a second server on the same database, both characters keep their job, their job level,
    // and their skills, and the changer still wears the staff.
    private void PlayAfterTheRestart(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var changer = new SocketClient(content, ChangerIdentity, ChangerName);
        using var mender = new SocketClient(content, MenderIdentity, MenderName);
        EnterTogether(port, changer, mender);

        AssertReadyToChange("restart", changer);
        AssertWearsTheStaff("restart", changer);
        AssertReadyToChange("restart", mender);
        AssertSees("restart", mender, changer, Adventurer);
        Assert.That(StoredJob(ChangerName), Is.EqualTo((Adventurer, 10, 0L)), "restart: the job stored");
        Assert.That(StoredJob(MenderName), Is.EqualTo((Adventurer, 10, 0L)), "restart: the job stored");
        AssertCleanTraffic("restart", changer, mender);
    }

    private static void EnterTogether(int port, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            client.Connect(port);
        }

        bool isEntered = SocketClients.PumpUntil(
            () => clients.All(client => client.Connection.World?.Inventory.IsCurrent == true
                && client.Connection.World.Sheet != null
                && client.Connection.World.SkillsReceivedAt > 0),
            clients);
        string states = string.Join(
            ", ",
            clients.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"enter: both entered with their sheets and skill lists ({states})");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        }
    }

    // Job level 10 grants 9 skill points, of which Strike 1, First Aid 1, and Focus 2 spent 4 (Gameplay Systems §9).
    private static void AssertReadyToChange(string step, SocketClient client)
    {
        CharacterSheet sheet = client.World.Sheet!;
        Assert.That((sheet.JobLevel, sheet.SkillPoints), Is.EqualTo(((byte)10, (byte)5)), $"{step}: 5 points left");
        Assert.That(
            client.World.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(new[] { (Strike, (byte)1), (FirstAid, (byte)1), (Focus, (byte)2) }),
            $"{step}: the Adventurer's tree as learned");
    }

    private static void AssertWearsTheStaff(string step, SocketClient client)
    {
        InventoryEntry staff = client.World.Inventory.Rows.Single(row => row.Item.Value == TrainingStaff);
        Assert.That(staff.Slot, Is.EqualTo(EquipmentSlot.Weapon), $"{step}: the staff worn");
    }

    // The viewer has a spawn for the seen character, of the job given, and draws it.
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string job)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: in view");
        RemoteEntity remote = viewer.World.Remotes[entity];
        Assert.That((remote.Kind, remote.DefinitionId), Is.EqualTo((EntityKind.Player, job)), $"{step}: its job");
    }

    private void StoreWornStaff(string name)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var insert = new NpgsqlCommand(
            "WITH staff AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + "version) SELECT id, @item, 1, 0, 0 FROM characters WHERE name = @name RETURNING character_id, id) "
            + "INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
            + "SELECT character_id, 'Weapon', id, 0 FROM staff",
            connection);
        insert.Parameters.AddWithValue("item", TrainingStaff);
        insert.Parameters.AddWithValue("name", name);
        Assert.That(insert.ExecuteNonQuery(), Is.EqualTo(1), "the staff stored as worn");
    }

    private (string Job, int JobLevel, long JobExperience) StoredJob(string name)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT job_definition_id, job_level, job_exp FROM characters WHERE name = @name",
            connection);
        command.Parameters.AddWithValue("name", name);
        using NpgsqlDataReader reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, $"{name} is stored");
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2));
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
    public void FirstJobs_OverRealSocketsAndPostgres_AreReachedFromAdventurerJobLevelTenAndKeptThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using (IHost first = StartHost(root.Path))
        {
            PlayBeforeTheChange(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
