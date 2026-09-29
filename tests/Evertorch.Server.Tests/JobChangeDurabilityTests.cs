using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A job change can be neither duplicated nor undone on PostgreSQL 18 (Milestone 10 verification; Gameplay Systems
///     §6.1; Persistence §6): a replayed or stale <see cref="ChangeJob" /> changes nothing more, a lost answer leaves
///     the next baseline to carry the change, a checkpoint built before the change never writes the old job back, a
///     change asked for while a quest reward is in flight waits for it, and a paused database keeps the change for
///     later. After each, a restart loads the first job at job level 1 with a build that spends no more than it may.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class JobChangeDurabilityTests
{
    [TearDown]
    public void EndOutage()
    {
        if (m_isPaused)
        {
            m_database.Resume();
            m_isPaused = false;
        }
    }

    private const string Guildmaster = "npc.guildmaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Hunt = "quest.crawler_hunt";
    private const string Adventurer = "job.adventurer";
    private const string Vanguard = "job.vanguard";
    private const string Arcanist = "job.arcanist";
    private const string TrainingStaff = "item.weapon.training_staff";
    private const int CommandTimeoutMs = 1000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private PostgresFixture m_database = null!;
    private bool m_isPaused;

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

    // The store as the host builds it, each connection bounded by the command timeout.
    private TestServer NewServer(int reconnectGraceMs = 0)
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: new PostgresGameStore(m_database.ConnectionString, TimeSpan.FromMilliseconds(CommandTimeoutMs)),
            reconnectGraceMs: reconnectGraceMs,
            withNpcs: true);
    }

    private long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private (string Job, long JobLevel, long JobExperience) StoredJob(long character)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            $"SELECT job_definition_id, job_level, job_exp FROM characters WHERE id = {character}",
            connection);
        using NpgsqlDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2));
    }

    private long StoredUnequips(long character)
    {
        return Scalar(
            $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} AND operation_type = 'unequip'");
    }

    // Ticks, with the database's work running on the thread pool, until the condition holds.
    private static void TickUntil(TestServer server, Func<bool> condition, string what)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Limit)
        {
            server.Tick();
            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.Fail($"Not reached within {Limit.TotalSeconds} s: {what}");
    }

    // Signs in as dev:<identity>, creates the character, and writes it at the Adventurer's job level 10, the cap a
    // change needs, straight to the database; with isStaffWorn it also wears the training staff, which no first job
    // but the Arcanist wields.
    private long Create(TestServer server, string identity, string name, bool isStaffWorn = false)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        Execute($"UPDATE characters SET job_level = 10, job_exp = 0 WHERE id = {character}");
        if (isStaffWorn)
        {
            Execute(
                "WITH staff AS (INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
                + $"version) VALUES ({character}, '{TrainingStaff}', 1, 0, 0) RETURNING character_id, id) "
                + "INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
                + "SELECT character_id, 'Weapon', id, 0 FROM staff");
        }

        server.Disconnect(connection);
        server.Tick();
        return character;
    }

    private static ConnectionId Enter(TestServer server, string identity, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    private static void Checkpoint(TestServer server, ConnectionId player)
    {
        server.Lifetime.QueueCheckpoint(server.SessionOf(player).Character!);
    }

    private static void StandBy(TestServer server, ConnectionId player, string npc)
    {
        NpcEntity entity = server.NpcOf(npc);
        server.Place(player, entity.Position.X + 2f, entity.Position.Z);
        server.Tick(2);
    }

    private static CharacterSheet LastSheet(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CharacterSheet)
            .Select(message => CharacterSheet.TryRead(message.Payload, out CharacterSheet? sheet) ? sheet : null)
            .Last(sheet => sheet != null)!;
    }

    // A new server on the same database loads the character: a first job at job level 1 whose build spends no more
    // than it may, so the load resets nothing.
    private (string Job, int JobLevel) Restart(string identity, long character)
    {
        TestServer restarted = NewServer();
        ConnectionId player = Enter(restarted, identity, character);
        PlayerEntity entity = restarted.PlayerOf(player);
        Assert.That(restarted.BuildsLog.Entries, Is.Empty, "no overspent build to reset");
        Assert.That(restarted.Builds.IsOverspent(entity), Is.False);
        return (entity.Job.Value, entity.JobLevel);
    }

    private void ChangeAt(TestServer server, ConnectionId player, string job, uint sequence)
    {
        server.SendChangeJob(player, server.NpcOf(Guildmaster).Id, job, sequence);
    }

    private static CommandRejectionReason LastRejection(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected.Reason;
            })
            .LastOrDefault();
    }

    // Asked for while the database is known to be down, the change is refused and changes nothing. Asked for again
    // just before the database pauses, its commit's answer is lost; once the database answers, the lookup settles it
    // either way, the character's job always the stored one, and a change asked for after that lands once
    // (Persistence §7).
    [Test]
    public void ChangeJob_AroundAPausedDatabase_IsRefusedOrSettled_NeverHalfDone()
    {
        TestServer server = NewServer();
        long character = Create(server, "v10-pause", "JobPause");
        ConnectionId player = Enter(server, "v10-pause", character);
        StandBy(server, player, Guildmaster);
        m_database.Pause();
        m_isPaused = true;
        server.Persistence.Probe();

        ChangeAt(server, player, Vanguard, 1);
        server.Tick();
        CommandRejectionReason refusedInTheOutage = LastRejection(server, player);
        string jobInTheOutage = server.PlayerOf(player).Job.Value;
        m_database.Resume();
        m_isPaused = false;
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return server.Persistence.State == DatabaseState.Available;
            },
            "the database available again");

        server.RunsPersistence = false;
        ChangeAt(server, player, Vanguard, 2);
        server.Tick();
        m_database.Pause();
        m_isPaused = true;
        server.RunsPersistence = true;
        server.Tick(5);
        string jobInThePause = server.PlayerOf(player).Job.Value;
        m_database.Resume();
        m_isPaused = false;
        TickUntil(server, () => server.SessionOf(player).Character!.Operation == null, "the commit settled");
        string settled = server.PlayerOf(player).Job.Value;
        string stored = StoredJob(character).Job;
        if (settled == Adventurer)
        {
            ChangeAt(server, player, Vanguard, 3);
        }

        TickUntil(
            server,
            () => server.PlayerOf(player).Job.Value == Vanguard
                && StoredJob(character).Job == Vanguard
                && server.SessionOf(player).Character!.Operation == null,
            "the change");

        Assert.That(refusedInTheOutage, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That((jobInTheOutage, jobInThePause), Is.EqualTo((Adventurer, Adventurer)), "nothing before a commit");
        Assert.That(settled, Is.EqualTo(stored), "the settled job is the stored one");
        Assert.That(StoredJob(character), Is.EqualTo((Vanguard, 1L, 0L)));
        Assert.That(Restart("v10-pause", character), Is.EqualTo((Vanguard, 1)));
    }

    [Test]
    public void ChangeJob_ReplayedOrStale_ChangesOnce_AndTheRestartKeepsIt()
    {
        TestServer server = NewServer();
        long character = Create(server, "v10-replay", "JobReplay", true);
        ConnectionId player = Enter(server, "v10-replay", character);
        StandBy(server, player, Guildmaster);

        ChangeAt(server, player, Vanguard, 2);
        ChangeAt(server, player, Vanguard, 2);
        ChangeAt(server, player, Arcanist, 1);
        server.Tick();
        TickUntil(
            server,
            () => server.SessionOf(player).Character!.Operation == null && StoredJob(character).Job == Vanguard,
            "the change committed");
        ChangeAt(server, player, Arcanist, 3);
        server.Tick();

        Assert.That(StoredJob(character), Is.EqualTo((Vanguard, 1L, 0L)));
        Assert.That(StoredUnequips(character), Is.EqualTo(1), "the staff taken off once");
        Assert.That(server.PlayerOf(player).Job.Value, Is.EqualTo(Vanguard));
        Assert.That(
            server.SessionOf(player).RefusedCommands,
            Is.EqualTo(3),
            "the replay and the stale one, unanswered, and a second change");
        Assert.That(Restart("v10-replay", character), Is.EqualTo((Vanguard, 1)));
    }

    // Sampled after every tick while the reward and then the change land: the stored job pair is always whole, and the
    // change, refused while the reward is in flight, goes through once it is settled.
    [Test]
    public void ChangeJob_WhileAQuestRewardIsInFlight_IsRefusedUntilItLands()
    {
        TestServer server = NewServer();
        long character = Create(server, "v10-reward", "JobReward");
        Execute(
            "INSERT INTO character_quests (character_id, quest_definition_id, state, progress, started_at, version) "
            + $"VALUES ({character}, '{Hunt}', 'active', 5, now(), 0)");
        ConnectionId player = Enter(server, "v10-reward", character);
        StandBy(server, player, GateWarden);
        server.RunsPersistence = false;

        server.SendCompleteQuest(player, server.NpcOf(GateWarden).Id, Hunt, 1);
        server.Tick();
        bool wasInFlight = server.SessionOf(player).Character!.Operation?.Kind == InventoryOperationKind.QuestReward;
        StandBy(server, player, Guildmaster);
        ChangeAt(server, player, Vanguard, 2);
        server.Tick();
        CommandRejectionReason refusal = LastRejection(server, player);
        server.RunsPersistence = true;
        TickUntil(
            server,
            () => server.SessionOf(player).Character!.Operation == null && server.Persistence.PendingJobs == 0,
            "the reward");
        ChangeAt(server, player, Vanguard, 3);
        TickUntil(
            server,
            () =>
            {
                (string job, long level, long _) = StoredJob(character);
                Assert.That(
                    (job, level),
                    Is.EqualTo((Adventurer, 10L)).Or.EqualTo((Vanguard, 1L)),
                    "only a whole job pair");
                return job == Vanguard && server.SessionOf(player).Character!.Operation == null;
            },
            "the change after the reward");

        Assert.That(wasInFlight, Is.True);
        Assert.That(refusal, Is.EqualTo(CommandRejectionReason.ItemActionInFlight), "9 while the reward is in flight");
        Assert.That(Scalar($"SELECT count(*) FROM character_quests WHERE character_id = {character} "
            + "AND state = 'completed'"), Is.EqualTo(1), "the reward kept");
        Assert.That(StoredJob(character), Is.EqualTo((Vanguard, 1L, 0L)));
        Assert.That(Restart("v10-reward", character), Is.EqualTo((Vanguard, 1)));
    }

    [Test]
    public void ChangeJob_WhoseAnswerIsLost_IsCarriedByTheNextBaseline_AndARetryChangesNothing()
    {
        TestServer server = NewServer(1000);
        long character = Create(server, "v10-lost", "JobLost");
        ConnectionId player = Enter(server, "v10-lost", character);
        StandBy(server, player, Guildmaster);
        ChangeAt(server, player, Vanguard, 1);
        server.Tick();
        server.Transport.ClearSent();

        server.Disconnect(player);
        TickUntil(server, () => StoredJob(character).Job == Vanguard, "the change committed without its owner");
        ConnectionId again = Enter(server, "v10-lost", character);
        server.Tick(2);
        CharacterSheet sheet = LastSheet(server, again);
        StandBy(server, again, Guildmaster);
        ChangeAt(server, again, Vanguard, 2);
        server.Tick();

        Assert.That(
            (sheet.Job.Value, sheet.JobLevel, sheet.SkillPoints),
            Is.EqualTo((Vanguard, (ushort)1, (ushort)9)),
            "the change, answered by the baseline");
        Assert.That(server.PlayerOf(again).Job.Value, Is.EqualTo(Vanguard), "the retry was refused");
        Assert.That(StoredJob(character), Is.EqualTo((Vanguard, 1L, 0L)));
        Assert.That(Restart("v10-lost", character), Is.EqualTo((Vanguard, 1)));
    }

    // A checkpoint queued before the change lands before it, and one built from the Adventurer at job level 10 while
    // the change is in flight writes nothing of the job: neither puts the Adventurer's job pair or its job level back
    // over the Vanguard's (Persistence §6).
    [Test]
    public void Checkpoints_BuiltBeforeAndDuringTheChange_NeverWriteTheOldJobBack()
    {
        TestServer server = NewServer();
        long character = Create(server, "v10-checkpoint", "JobCheckpoint");
        ConnectionId player = Enter(server, "v10-checkpoint", character);
        StandBy(server, player, Guildmaster);
        Checkpoint(server, player);
        TickUntil(server, () => server.Persistence.PendingJobs == 0, "the checkpoint before the change");
        server.RunsPersistence = false;

        ChangeAt(server, player, Vanguard, 1);
        server.Tick();
        bool wasInFlight = server.SessionOf(player).Character!.Operation?.Kind == InventoryOperationKind.JobChange;
        Checkpoint(server, player);
        server.RunsPersistence = true;
        TickUntil(
            server,
            () =>
            {
                (string job, long level, long _) = StoredJob(character);
                Assert.That(
                    (job, level),
                    Is.EqualTo((Adventurer, 10L)).Or.EqualTo((Vanguard, 1L)),
                    "only a whole job pair");
                return server.SessionOf(player).Character!.Operation == null
                    && server.Persistence.PendingJobs == 0
                    && job == Vanguard;
            },
            "both checkpoints and the change");
        Checkpoint(server, player);
        TickUntil(server, () => server.Persistence.PendingJobs == 0, "the checkpoint after the change");

        Assert.That(wasInFlight, Is.True, "the second checkpoint was built with the change in flight");
        Assert.That(StoredJob(character), Is.EqualTo((Vanguard, 1L, 0L)));
        Assert.That(Restart("v10-checkpoint", character), Is.EqualTo((Vanguard, 1)));
    }
}
}
