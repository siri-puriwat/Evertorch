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
///     The Milestone 10 first jobs (ROADMAP §8) end to end: the composed server host on a real PostgreSQL 18, real UDP
///     sockets on loopback, and two clients built from the client's production networking and gameplay code, pumped
///     side by side on the test's thread. A changer stored at Adventurer job level 10, with skill points left unspent
///     and the training staff worn, enters beside a second character at the same level, which sees it. The changer
///     walks up to the Guildmaster and becomes a Vanguard: the staff comes off, its unspent points carry over, it
///     learns Heavy Blow, and the second character sees the Vanguard's body in place of the Adventurer's. The second
///     becomes an Arcanist, learns Mend, selects the Vanguard, and Mends it. Both keep what they hold through a
///     restart.
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
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string Guildmaster = "npc.guildmaster";
    private const string HeavyBlow = "skill.heavy_blow";
    private const string WarCry = "skill.war_cry";
    private const string IronGuard = "skill.iron_guard";
    private const string ArcaneBolt = "skill.arcane_bolt";
    private const string Clarity = "skill.clarity";
    private const string Mend = "skill.mend";
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
    ///     training staff and hurt; they enter the training ground and see each other; the changer becomes a Vanguard
    ///     and learns Heavy Blow; the mender becomes an Arcanist and Mends it; and the server stops.
    /// </summary>
    private void PlayTheChange(IHost host)
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
            StoreHealth(ChangerName, 20);
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

        ChangeToVanguard(changer, mender);
        LearnHeavyBlow(changer, mender);
        MendTheVanguard(changer, mender);
        Assert.That(
            SocketClients.PumpUntil(
                () => admin.GetPlayers(AdminActor.LocalConsole)
                    .Any(player => player.Entity == changer.World.LocalEntity && player.Job.Value == Vanguard),
                changer,
                mender),
            Is.True,
            "change: the console names the job");

        host.StopAsync().GetAwaiter().GetResult();
        Assert.That(
            SocketClients.PumpUntil(
                () => changer.Connection.State == ClientConnectionState.Disconnected
                    && mender.Connection.State == ClientConnectionState.Disconnected,
                changer,
                mender),
            Is.True,
            "stop: both clients heard the server stop");
        AssertCleanTraffic("change", changer, mender);
    }

    // The changer walks up to the Guildmaster, which offers both first jobs from job level 10, and becomes a Vanguard:
    // the commit takes the staff off, the sheet names the new job at job level 1 with the 5 points it carried, the
    // skill list names the whole tree, and the second character sees the new body in place of the old, with no
    // despawn (Gameplay Systems §6.1; Network Protocol §9).
    private static void ChangeToVanguard(SocketClient changer, SocketClient mender)
    {
        const string step = "change";
        ClientWorld world = changer.World;
        Assert.That(
            SocketClients.PumpUntil(() => GuildmasterInView(world) != null, changer, mender),
            Is.True,
            $"{step}: the Guildmaster in view");
        RemoteEntity guildmaster = GuildmasterInView(world)!;
        world.TryGetNpcServices(guildmaster.Entity, out NpcServices? services);
        Assert.That(
            services!.JobChanges.Select(change => (change.Job.Value, change.FromJob.Value, change.Level)),
            Is.EqualTo(new[] { (Arcanist, Adventurer, (ushort)10), (Vanguard, Adventurer, (ushort)10) }),
            $"{step}: both first jobs offered");
        int windows = changer.NpcWindows.Count;
        changer.TalkTo(guildmaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => changer.NpcWindows.Count > windows, changer, mender),
            Is.True,
            $"{step}: walked up to it");

        var despawned = new List<EntityId>();
        mender.World.RemoteDespawned += remote => despawned.Add(remote.Entity);
        changer.Connection.SendChangeJob(guildmaster.Entity, new JobDefinitionId(Vanguard));
        Assert.That(
            SocketClients.PumpUntil(() => world.Sheet!.Job.Value == Vanguard, changer, mender),
            Is.True,
            $"{step}: the sheet names the Vanguard; {world.LastRejection}");
        Assert.That(
            (world.Sheet!.JobLevel, world.Sheet.SkillPoints),
            Is.EqualTo(((ushort)1, (ushort)5)),
            $"{step}: job level 1, the 5 points carried");
        Assert.That(
            SocketClients.PumpUntil(() => world.Skills.Count == 6, changer, mender),
            Is.True,
            $"{step}: the whole tree listed");
        Assert.That(
            world.Inventory.Rows.Single(row => row.Item.Value == TrainingStaff).Slot,
            Is.EqualTo(EquipmentSlot.None),
            $"{step}: the staff taken off, still held");
        Assert.That(
            SocketClients.PumpUntil(
                () => mender.World.Remotes.TryGetValue(world.LocalEntity, out RemoteEntity? seen)
                    && seen.DefinitionId == Vanguard,
                changer,
                mender),
            Is.True,
            $"{step}: the other sees the Vanguard");
        Assert.That(despawned, Has.Count.EqualTo(1), $"{step}: the old body replaced once, in place");
        Assert.That(mender.World.Sheet!.Job.Value, Is.EqualTo(Adventurer), $"{step}: the other's own job unchanged");
    }

    // A skill of the Vanguard's own tree takes one of the carried points (Gameplay Systems §9).
    private static void LearnHeavyBlow(SocketClient changer, SocketClient mender)
    {
        const string step = "learn";
        ClientWorld world = changer.World;
        changer.Connection.SendLearnSkill(new SkillDefinitionId(HeavyBlow));
        Assert.That(
            SocketClients.PumpUntil(
                () => world.Skills.Any(entry => entry.Skill.Value == HeavyBlow && entry.Level == 1)
                    && world.Sheet!.SkillPoints == 4,
                changer,
                mender),
            Is.True,
            $"{step}: Heavy Blow 1 for a point; {world.LastRejection}");
    }

    // The mender walks up to the Guildmaster and becomes an Arcanist, learns Arcane Bolt, Clarity, and Mend with three
    // of its five carried points, selects the Vanguard, and Mends it: the Vanguard is told its health, 40 HP more
    // (Gameplay Systems §6, §9; Network Protocol §9).
    private static void MendTheVanguard(SocketClient changer, SocketClient mender)
    {
        const string step = "mend";
        ClientWorld world = mender.World;
        RemoteEntity guildmaster = GuildmasterInView(world)!;
        int windows = mender.NpcWindows.Count;
        mender.TalkTo(guildmaster.Entity);
        Assert.That(
            SocketClients.PumpUntil(() => mender.NpcWindows.Count > windows, changer, mender),
            Is.True,
            $"{step}: walked up to the Guildmaster");
        mender.Connection.SendChangeJob(guildmaster.Entity, new JobDefinitionId(Arcanist));
        Assert.That(
            SocketClients.PumpUntil(() => world.Sheet!.Job.Value == Arcanist, changer, mender),
            Is.True,
            $"{step}: the sheet names the Arcanist; {world.LastRejection}");
        foreach (string skill in new[] { ArcaneBolt, Clarity, Mend })
        {
            mender.Connection.SendLearnSkill(new SkillDefinitionId(skill));
            Assert.That(
                SocketClients.PumpUntil(
                    () => world.Skills.Any(entry => entry.Skill.Value == skill && entry.Level == 1),
                    changer,
                    mender),
                Is.True,
                $"{step}: {skill} 1 learned; {world.LastRejection}");
        }

        EntityId vanguard = changer.World.LocalEntity;
        mender.Connection.SendTarget(vanguard);
        Assert.That(
            SocketClients.PumpUntil(() => world.Target == vanguard, changer, mender),
            Is.True,
            $"{step}: the Vanguard selected");
        Assert.That(
            mender.DistanceTo(changer.World.Predictor.Position),
            Is.LessThanOrEqualTo(6f),
            $"{step}: within Mend's reach");
        uint before = changer.World.LocalHealth;
        uint healed = Math.Min(changer.World.LocalMaximumHealth, before + 40);
        Assert.That(before, Is.LessThan(changer.World.LocalMaximumHealth), $"{step}: the Vanguard hurt");
        mender.Connection.SendUseSkill(new SkillDefinitionId(Mend), vanguard);
        Assert.That(
            SocketClients.PumpUntil(() => changer.World.LocalHealth >= healed, changer, mender),
            Is.True,
            $"{step}: the Vanguard told of 40 HP more than {before}; {world.LastRejection}");
    }

    private static RemoteEntity? GuildmasterInView(ClientWorld world)
    {
        return world.Remotes.Values.SingleOrDefault(candidate =>
            candidate.Kind == EntityKind.Npc && candidate.DefinitionId == Guildmaster);
    }

    // After a clean stop and a second server on the same database, the changer is still a Vanguard at job level 1
    // with Heavy Blow and the staff unworn, which the second character sees; the second is still an Arcanist at job
    // level 1 with Mend, which the first sees.
    private void PlayAfterTheRestart(IHost host)
    {
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var changer = new SocketClient(content, ChangerIdentity, ChangerName);
        using var mender = new SocketClient(content, MenderIdentity, MenderName);
        EnterTogether(port, changer, mender);

        CharacterSheet sheet = changer.World.Sheet!;
        Assert.That(
            (sheet.Job.Value, sheet.JobLevel, sheet.SkillPoints),
            Is.EqualTo((Vanguard, (ushort)1, (ushort)4)),
            "restart: still a Vanguard");
        Assert.That(
            changer.World.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(
                new[]
                {
                    (Strike, (byte)1), (FirstAid, (byte)1), (Focus, (byte)2), (HeavyBlow, (byte)1), (WarCry, (byte)0),
                    (IronGuard, (byte)0)
                }),
            "restart: both trees as learned");
        Assert.That(
            changer.World.Inventory.Rows.Single(row => row.Item.Value == TrainingStaff).Slot,
            Is.EqualTo(EquipmentSlot.None),
            "restart: the staff still off");
        CharacterSheet mended = mender.World.Sheet!;
        Assert.That(
            (mended.Job.Value, mended.JobLevel, mended.SkillPoints),
            Is.EqualTo((Arcanist, (ushort)1, (ushort)2)),
            "restart: still an Arcanist");
        Assert.That(
            mender.World.Skills.Select(entry => (entry.Skill.Value, entry.Level)),
            Is.EqualTo(
                new[]
                {
                    (Strike, (byte)1), (FirstAid, (byte)1), (Focus, (byte)2), (ArcaneBolt, (byte)1), (Clarity, (byte)1),
                    (Mend, (byte)1)
                }),
            "restart: the Arcanist's trees as learned");
        AssertSees("restart", mender, changer, Vanguard);
        AssertSees("restart", changer, mender, Arcanist);
        Assert.That(StoredJob(ChangerName), Is.EqualTo((Vanguard, 1, 0L)), "restart: the change stored");
        Assert.That(StoredJob(MenderName), Is.EqualTo((Arcanist, 1, 0L)), "restart: the mender's change stored");
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
        Assert.That((sheet.JobLevel, sheet.SkillPoints), Is.EqualTo(((ushort)10, (ushort)5)), $"{step}: 5 points left");
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

    private void StoreHealth(string name, int health)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var update = new NpgsqlCommand("UPDATE characters SET hp = @hp WHERE name = @name", connection);
        update.Parameters.AddWithValue("hp", health);
        update.Parameters.AddWithValue("name", name);
        Assert.That(update.ExecuteNonQuery(), Is.EqualTo(1), $"{name}'s health stored");
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
    public void FirstJobs_OverRealSocketsAndPostgres_AreReachedAtTheGuildmasterAndKeptThroughARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using (IHost first = StartHost(root.Path))
        {
            PlayTheChange(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
