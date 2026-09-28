using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Evertorch.Rules;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Retries cannot duplicate points on PostgreSQL 18 (Milestone 9 verification; Persistence §6, §7): a replayed or
///     stale <see cref="AllocateStat" /> or <see cref="LearnSkill" /> spends nothing, a lost answer leaves the next
///     baseline to carry the result, a reset with a checkpoint in flight is what the database keeps, raises made while
///     a quest reward is in flight wait for the checkpoint after it, and a paused database keeps the checkpoint for
///     later. After each, a restart loads a build that spends no more than its levels grant.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BuildDurabilityTests
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
    private const string Strike = "skill.strike";
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

    private long StoredAgility(long character)
    {
        return Scalar($"SELECT agi FROM characters WHERE id = {character}");
    }

    private long StoredStrike(long character)
    {
        return Scalar(
            "SELECT coalesce(max(level), 0) FROM character_skills "
            + $"WHERE character_id = {character} AND skill_definition_id = '{Strike}'");
    }

    private long StoredSkillRows(long character)
    {
        return Scalar($"SELECT count(*) FROM character_skills WHERE character_id = {character}");
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

    // Signs in as dev:<identity>, creates the character, and writes the levels a fight would have left it straight to
    // the database: base level 10 grants 34 stat points and job level 6 five skill points.
    private long Create(TestServer server, string identity, string name, int level = 10, int jobLevel = 6)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        Execute($"UPDATE characters SET base_level = {level}, job_level = {jobLevel} WHERE id = {character}");
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

    // A new server on the same database loads the character: its build spends no more than its levels grant, so the
    // load resets nothing, and it holds what the database kept.
    private (PrimaryStats Stats, int Strike) Restart(string identity, long character)
    {
        TestServer restarted = NewServer();
        ConnectionId player = Enter(restarted, identity, character);
        PlayerEntity entity = restarted.PlayerOf(player);
        Assert.That(restarted.BuildsLog.Entries, Is.Empty, "no overspent build to reset");
        Assert.That(restarted.Builds.IsOverspent(entity), Is.False, "spent within what was earned");
        entity.Skills.TryGetValue(new SkillDefinitionId(Strike), out int strike);
        return (entity.Primary, strike);
    }

    private static CharacterSheet LastSheet(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CharacterSheet)
            .Select(message => CharacterSheet.TryRead(message.Payload, out CharacterSheet? sheet) ? sheet : null)
            .Last(sheet => sheet != null)!;
    }

    private static SkillList LastSkillList(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.SkillList)
            .Select(message => SkillList.TryRead(message.Payload, out SkillList? list) ? list : null)
            .Last(list => list != null)!;
    }

    [Test]
    public void AllocateAndLearn_ReplayedOrStale_SpendOnce_AndTheRestartKeepsIt()
    {
        TestServer server = NewServer();
        long character = Create(server, "v9-replay", "BuildReplay");
        ConnectionId player = Enter(server, "v9-replay", character);

        server.SendAllocateStat(player, PrimaryStat.Agi, 1, 1);
        server.SendAllocateStat(player, PrimaryStat.Agi, 1, 1);
        server.SendLearnSkill(player, Strike, 2);
        server.SendLearnSkill(player, Strike, 2);
        server.SendLearnSkill(player, Strike, 1);
        server.Tick();
        Checkpoint(server, player);
        TickUntil(server, () => StoredAgility(character) == 6 && StoredStrike(character) == 1, "the checkpoint");

        Assert.That(server.PlayerOf(player).Primary.Agi, Is.EqualTo(6), "one raise");
        Assert.That(server.PlayerOf(player).Skills[new SkillDefinitionId(Strike)], Is.EqualTo(1), "one level");
        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(3),
            "the replays and the stale one, unanswered");
        (PrimaryStats stats, int strike) = Restart("v9-replay", character);
        Assert.That((stats.Agi, strike), Is.EqualTo((6, 1)));
    }

    [Test]
    public void AllocateAndLearn_WhoseAnswersAreLost_AreCarriedByTheNextBaseline_AndARetrySpendsNothing()
    {
        TestServer server = NewServer(1000);
        long character = Create(server, "v9-lost", "BuildLost");
        ConnectionId player = Enter(server, "v9-lost", character);
        server.SendAllocateStat(player, PrimaryStat.Agi, 1, 1);
        server.SendLearnSkill(player, Strike, 2);
        server.Tick();
        server.Transport.ClearSent();

        server.Disconnect(player);
        server.Tick();
        ConnectionId again = Enter(server, "v9-lost", character);
        server.Tick(2);
        CharacterSheet sheet = LastSheet(server, again);
        SkillList list = LastSkillList(server, again);
        server.SendAllocateStat(again, PrimaryStat.Agi, 1, 1);
        server.SendLearnSkill(again, Strike, 2);
        server.Tick();

        Assert.That((sheet.Stats[1].Value, sheet.StatPoints), Is.EqualTo(((byte)6, (ushort)32)), "the raise, answered");
        Assert.That((list.Skills[0].Level, sheet.SkillPoints), Is.EqualTo(((byte)1, (byte)4)), "the level, answered");
        Assert.That(server.PlayerOf(again).Primary.Agi, Is.EqualTo(6), "the retry was a replay");
        Assert.That(server.PlayerOf(again).Skills[new SkillDefinitionId(Strike)], Is.EqualTo(1));
        Checkpoint(server, again);
        TickUntil(server, () => StoredAgility(character) == 6 && StoredStrike(character) == 1, "the checkpoint");
        (PrimaryStats stats, int strike) = Restart("v9-lost", character);
        Assert.That((stats.Agi, strike), Is.EqualTo((6, 1)));
    }

    [Test]
    public void Checkpoint_OfABuild_QueuedWhileTheDatabaseIsPaused_LandsOnceItAnswers()
    {
        TestServer server = NewServer();
        long character = Create(server, "v9-pause", "BuildPause");
        ConnectionId player = Enter(server, "v9-pause", character);
        m_database.Pause();
        m_isPaused = true;
        server.Persistence.Probe();

        server.SendAllocateStat(player, PrimaryStat.Vit, 3, 1);
        server.SendLearnSkill(player, Strike, 2);
        server.Tick();
        Checkpoint(server, player);
        server.Tick(5);
        m_database.Resume();
        m_isPaused = false;
        TickUntil(server, () => Scalar($"SELECT vit FROM characters WHERE id = {character}") == 8, "the checkpoint");

        Assert.That(StoredStrike(character), Is.EqualTo(1));
        (PrimaryStats stats, int strike) = Restart("v9-pause", character);
        Assert.That((stats.Vit, strike), Is.EqualTo((8, 1)));
    }

    // Sampled after every tick while the reward and the checkpoint queued beside it land: the stored statistics never
    // cost more than the stored base level grants (Persistence §6).
    [Test]
    public void Raises_MadeWhileAQuestRewardIsInFlight_NeverStoreMoreSpentThanEarned()
    {
        TestServer server = NewServer();
        long character = Create(server, "v9-reward", "BuildReward", 2, 1);
        Execute(
            "INSERT INTO character_quests (character_id, quest_definition_id, state, progress, started_at, version) "
            + $"VALUES ({character}, '{Hunt}', 'active', 5, now(), 0)");
        ConnectionId player = Enter(server, "v9-reward", character);
        StandBy(server, player, GateWarden);
        var rules = new RenewalProgressionRules();
        server.RunsPersistence = false;

        server.SendCompleteQuest(player, server.NpcOf(GateWarden).Id, Hunt, 1);
        server.Tick();
        server.SendAllocateStat(player, PrimaryStat.Agi, 1, 2);
        server.Tick();
        bool wasInFlight = server.SessionOf(player).Character!.Operation?.Kind == InventoryOperationKind.QuestReward;
        Checkpoint(server, player);
        server.RunsPersistence = true;
        int samples = 0;
        TickUntil(
            server,
            () =>
            {
                long agility = StoredAgility(character);
                long level = Scalar($"SELECT base_level FROM characters WHERE id = {character}");
                int spent = agility > 5 ? rules.StatRaiseCost(5) : 0;
                Assert.That(spent, Is.LessThanOrEqualTo(rules.StatPointsGranted((int)level)), $"at level {level}");
                samples++;
                return server.SessionOf(player).Character!.Operation == null
                    && server.Persistence.PendingJobs == 0
                    && agility == 6
                    && level == 4;
            },
            "the reward's two levels (2 to 4 with 150), then AGI 6 stored");

        Assert.That(wasInFlight, Is.True, "the raise was made with the reward in flight");
        Assert.That(samples, Is.GreaterThan(1));
        (PrimaryStats stats, int _) = Restart("v9-reward", character);
        Assert.That(stats.Agi, Is.EqualTo(6));
    }

    [Test]
    public void Reset_WithACheckpointInFlight_IsWhatTheDatabaseKeeps()
    {
        TestServer server = NewServer();
        long character = Create(server, "v9-reset", "BuildReset");
        ConnectionId player = Enter(server, "v9-reset", character);
        StandBy(server, player, Guildmaster);
        server.RunsPersistence = false;

        server.SendAllocateStat(player, PrimaryStat.Agi, 5, 1);
        server.SendLearnSkill(player, Strike, 2);
        server.Tick();
        Checkpoint(server, player);
        server.SendResetBuild(player, server.NpcOf(Guildmaster).Id, 3);
        server.Tick();
        server.RunsPersistence = true;
        TickUntil(
            server,
            () => StoredAgility(character) == 5
                && StoredSkillRows(character) == 0
                && server.Persistence.PendingJobs == 0,
            "both checkpoints, the reset's last");

        Assert.That(server.PlayerOf(player).Primary.Agi, Is.EqualTo(5));
        (PrimaryStats stats, int strike) = Restart("v9-reset", character);
        Assert.That((stats.Agi, strike), Is.EqualTo((5, 0)), "the reset kept");
    }
}
}
