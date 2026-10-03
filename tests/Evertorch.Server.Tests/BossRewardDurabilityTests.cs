using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A boss's prize reaches its most valuable player's bag once, and only once, on PostgreSQL 18 (Milestone 13
///     verification; Gameplay Systems §11; Persistence §5, §7, §9): lost answers are settled from the ledger, a paused
///     database holds the grant and never drops it, a full bag leaves a drop the ledger lets into one bag, and a grant
///     waits behind an operation in flight, and a logout, a grace's end, an expulsion, a crossing, and a re-entry each
///     wait for it or keep it. A restart loads the prize where it landed.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BossRewardDurabilityTests
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

    private const string Mantle = "item.armor.monarch_mantle";
    private const string Gel = "item.material.slime_gel";
    private const int CommandTimeoutMs = 1000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

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

    // The store as the host builds it, each connection bounded by the command timeout; the scripted drops' first draw
    // keeps the Mantle, and the AI is off.
    private TestServer NewServer(IGameStore? store = null, int reconnectGraceMs = 0)
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: store ?? NewStore(),
            reconnectGraceMs: reconnectGraceMs,
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0));
    }

    private PostgresGameStore NewStore()
    {
        return new PostgresGameStore(m_database.ConnectionString, TimeSpan.FromMilliseconds(CommandTimeoutMs));
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

    private long StoredMantles(long character)
    {
        return StoredRows(character, Mantle);
    }

    private long StoredRows(long character, string item)
    {
        return Scalar(
            "SELECT count(*) FROM inventory_items "
            + $"WHERE character_id = {character} AND item_definition_id = '{item}'");
    }

    private long StoredGrants(long character)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = 'boss_reward'");
    }

    private void Pause()
    {
        m_database.Pause();
        m_isPaused = true;
    }

    private void Resume()
    {
        m_database.Resume();
        m_isPaused = false;
    }

    // Ticks, the database's work running within each tick, until the condition holds.
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

    // Signs in as dev:<identity> and creates the character, without entering the world.
    private static long Create(TestServer server, string identity, string name)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        server.TickUntil(() => server.SessionOf(connection).Characters != null);
        server.SendCreateCharacter(connection, name);
        server.TickUntil(() => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name));
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
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

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    // The player enters the grotto, its hits make it the boss's MVP, and the boss dies: its prize waits on the player.
    private static CharacterSession EnterAndWin(TestServer server, string identity, long character)
    {
        ConnectionId player = Enter(server, identity, character);
        server.CrossIntoTheGrotto(player);
        return Win(server, player);
    }

    private static CharacterSession Win(TestServer server, ConnectionId player)
    {
        PlayerEntity entity = server.PlayerOf(player);
        MonsterEntity monarch = GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch);
        monarch.LogMvpDealt(entity.Character, 500);
        server.Combat.Kill(GrottoOf(server), monarch, entity, server.CurrentTick);
        return CharacterOf(server, player);
    }

    // A new server on the same database: the character enters with what is stored.
    private int MantlesAfterRestart(string identity, long character)
    {
        TestServer restarted = NewServer();
        ConnectionId player = Enter(restarted, identity, character);
        return CharacterOf(restarted, player).Inventory.Rows.Count(row => row.Item.Value == Mantle);
    }

    private static long AgeMs(TestServer server, ItemDropEntity drop)
    {
        return (server.CurrentTick - drop.DroppedTick) * 1000L / TestServer.TickRate;
    }

    private static int MantlesOnTheGround(TestServer server)
    {
        return server.World.Maps.SelectMany(map => map.ItemDrops).Count(drop => drop.Item.Value == Mantle);
    }

    // A grant still waiting when the reconnect grace ends, or when its MVP is expelled, holds the character in the
    // world until it lands; then the character is checkpointed and removed, the prize stored.
    [TestCase(false)]
    [TestCase(true)]
    public void Grant_PendingAtTheGracesEnd_OrForAnExpelledMvp_LandsBeforeTheRemoval(bool isExpelled)
    {
        TestServer server = NewServer(reconnectGraceMs: 500);
        string identity = isExpelled ? "v13-expelled" : "v13-grace";
        long character = Create(server, identity, isExpelled ? "PrizeExpelled" : "PrizeGrace");
        ConnectionId player = Enter(server, identity, character);
        server.CrossIntoTheGrotto(player);
        server.RunsPersistence = false;
        CharacterSession mvp = Win(server, player);
        if (isExpelled)
        {
            int violations = new AbuseOptions().ViolationThreshold / ViolationScore.Points;
            for (int index = 0; index < violations; index++)
            {
                server.Inbound.OnPayload(player, ProtocolChannel.Control, new byte[] { 0xFF, 0x7F, 0x01 });
            }
        }
        else
        {
            server.Disconnect(player);
        }

        server.Tick(TestServer.TickRate);
        bool isWaiting = GrottoOf(server).Players.Count == 1 && mvp.HasInventoryWork;
        server.RunsPersistence = true;
        TickUntil(server, () => GrottoOf(server).Players.Count == 0, "removed after the grant");

        Assert.That(isWaiting, Is.True, "the character waited for its prize");
        Assert.That(mvp.IsExpelled, Is.EqualTo(isExpelled));
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
    }

    // A pickup in flight keeps the bag; the grant follows it; a logout asked for meanwhile waits for both, and the
    // restart finds both items.
    [Test]
    public void Grant_BehindAPickupInFlight_FollowsIt_AndALogoutWaitsForBoth()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-behind", "PrizeBehind");
        ConnectionId player = Enter(server, "v13-behind", character);
        server.CrossIntoTheGrotto(player);
        CharacterSession mvp = CharacterOf(server, player);
        ItemDropEntity gel = server.World.SpawnItemDrop(
            mvp.Map,
            new ItemDefinitionId(Gel),
            2,
            mvp.Player.Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.RunsPersistence = false;
        server.SendPickup(player, gel.Id, 1);
        server.Tick();
        Win(server, player);
        server.SendLogout(player, 2);
        server.Tick(2);
        InventoryOperationKind? first = mvp.Operation?.Kind;
        server.RunsPersistence = true;

        TickUntil(server, () => server.SessionOf(player).State == SessionState.Authenticated, "logged out");

        Assert.That(first, Is.EqualTo(InventoryOperationKind.Pickup), "the pickup kept the bag");
        Assert.That(StoredRows(character, Gel), Is.EqualTo(1));
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
        Assert.That(MantlesAfterRestart("v13-behind", character), Is.EqualTo(1));
    }

    // A full bag leaves the prize at the MVP's feet under the grant's operation ID. Another player picks it up once
    // the MVP's 10 s are over, and the ledger then holds that ID once, as the other's pickup: the grant can never land
    // after it.
    [Test]
    public void Grant_IntoAFullBag_LeavesADrop_ThatTheLedgerLetsIntoOneBag()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-full", "PrizeFull");
        long other = Create(server, "v13-other", "PrizeOther");
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {character}, '{Gel}', 1, 0, 0 FROM generate_series(1, {PickupSystem.MaxInventoryRows})");
        ConnectionId winner = Enter(server, "v13-full", character);
        ConnectionId picker = Enter(server, "v13-other", other);
        server.CrossIntoTheGrotto(winner);
        server.CrossIntoTheGrotto(picker);
        server.PlayerOf(picker).Position = server.PlayerOf(winner).Position;

        CharacterSession mvp = Win(server, winner);
        Guid grant = mvp.PendingGrants.Peek().OperationId;
        TickUntil(server, () => !mvp.HasInventoryWork, "the grant settled");
        ItemDropEntity prize = server.World.Maps.SelectMany(map => map.ItemDrops)
            .Single(drop => drop.Item.Value == Mantle);
        TickUntil(
            server,
            () => AgeMs(server, prize) >= BossRewardSystem.PrizePriorityMs,
            "the MVP's 10 s over");
        server.SendPickup(picker, prize.Id, 1);
        TickUntil(server, () => MantlesOnTheGround(server) == 0, "picked up");
        InventoryResult replay = NewStore()
            .CommitGrantAsync(
                new GrantCommit(prize.DropId, character, Mantle, 1, 1, PickupSystem.MaxInventoryRows, DateTime.UtcNow),
                default)
            .GetAwaiter()
            .GetResult();

        Assert.That(prize.DropId, Is.EqualTo(grant), "the drop carries the grant's ID");
        Assert.That(
            Scalar($"SELECT count(*) FROM economy_ledger WHERE operation_id = '{prize.DropId}'"),
            Is.EqualTo(1));
        Assert.That(
            Scalar($"SELECT actor_character_id FROM economy_ledger WHERE operation_id = '{prize.DropId}'"),
            Is.EqualTo(other));
        Assert.That(replay.Status, Is.EqualTo(InventoryStatus.TakenByOther), "the grant cannot land after the pickup");
        Assert.That((StoredMantles(character), StoredMantles(other)), Is.EqualTo((0L, 1L)));
    }

    // An MVP standing in the grotto's portal crosses only once its prize has landed.
    [Test]
    public void Grant_Pending_HoldsAnMvpInAPortal_UntilItLands()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-portal", "PrizePortal");
        ConnectionId player = Enter(server, "v13-portal", character);
        server.CrossIntoTheGrotto(player);
        server.Tick(TestServer.TickRate + 1);
        server.RunsPersistence = false;
        CharacterSession mvp = Win(server, player);
        MapPortal portal = GrottoOf(server).Definition.Portals.Single();
        server.PlayerOf(player).Position = portal.Center;

        server.Tick(3);
        MapDefinitionId meanwhile = mvp.Map.Definition.Id;
        server.RunsPersistence = true;
        TickUntil(server, () => mvp.Map.Definition.Id != Grotto, "crossed");

        Assert.That(meanwhile, Is.EqualTo(Grotto), "held in the portal");
        Assert.That(mvp.Map.Definition.Id, Is.EqualTo(portal.DestinationMap));
        Assert.That(mvp.Inventory.Rows.Count(row => row.Item.Value == Mantle), Is.EqualTo(1));
        Assert.That(StoredGrants(character), Is.EqualTo(1));
    }

    // The MVP disconnects with its prize still waiting, and its removal waits for it; entering again from a connection
    // signed in beforehand takes the character back, and the prize lands in the bag it comes back to.
    [Test]
    public void Grant_Pending_WhenTheMvpEntersAgainDuringTheRemovalItWaitsFor_LandsInTheSameCharacter()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-again", "PrizeAgain");
        ConnectionId player = Enter(server, "v13-again", character);
        server.CrossIntoTheGrotto(player);
        ConnectionId again = server.Connect();
        server.SignIn(again, "dev:v13-again");
        server.TickUntil(() => server.SessionOf(again).Characters != null);
        server.RunsPersistence = false;
        CharacterSession mvp = Win(server, player);
        server.Disconnect(player);
        server.Tick(2);
        bool isDeferred = mvp.IsRemovalDeferred;

        server.SendEnterWorld(again, character);
        server.TickUntil(() => server.SessionOf(again).State == SessionState.InWorld);
        server.RunsPersistence = true;
        TickUntil(server, () => !mvp.HasInventoryWork, "the grant settled");
        server.Tick(TestServer.TickRate);

        Assert.That(isDeferred, Is.True, "the removal waited for the prize");
        Assert.That(CharacterOf(server, again), Is.SameAs(mvp), "the same character, taken back");
        Assert.That(GrottoOf(server).Players, Has.Count.EqualTo(1), "still in the world");
        Assert.That(mvp.Inventory.Rows.Count(row => row.Item.Value == Mantle), Is.EqualTo(1));
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
    }

    // The database pauses with the grant's commit queued: it is cut short and held, never dropped; once the database
    // answers, the ledger settles it and the grant lands once.
    [Test]
    public void Grant_ThroughAPausedDatabase_IsHeld_ThenLandsOnce_NeverOnTheGround()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-pause", "PrizePause");
        ConnectionId player = Enter(server, "v13-pause", character);
        server.CrossIntoTheGrotto(player);
        server.RunsPersistence = false;
        CharacterSession mvp = Win(server, player);
        server.Tick();
        Pause();
        server.RunsPersistence = true;

        server.Tick(5);
        bool isHeld = mvp.PendingGrants.Count == 1 && MantlesOnTheGround(server) == 0;
        Resume();
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return !mvp.HasInventoryWork;
            },
            "the grant settled after the pause");

        Assert.That(isHeld, Is.True, "held through the pause");
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
        Assert.That(MantlesOnTheGround(server), Is.Zero);
        Assert.That(MantlesAfterRestart("v13-pause", character), Is.EqualTo(1));
    }

    // Every answer of the grant's commit is lost: the writer replays it, which the ledger answers with the first, and
    // then the server asks the ledger, which knows it. One row in the bag and one ledger row, however many answers were
    // lost.
    [Test]
    public void Grant_WhoseAnswersAreLost_IsSettledFromTheLedgerOnce_AndTheRestartKeepsIt()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store);
        long character = Create(server, "v13-lost", "PrizeLost");
        store.IsLosingAnswers = true;

        CharacterSession mvp = EnterAndWin(server, "v13-lost", character);
        TickUntil(server, () => !mvp.HasInventoryWork, "the grant settled from the ledger");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(mvp.Inventory.Rows.Count(row => row.Item.Value == Mantle), Is.EqualTo(1));
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
        Assert.That(MantlesOnTheGround(server), Is.Zero);
        Assert.That(MantlesAfterRestart("v13-lost", character), Is.EqualTo(1));
    }

    // A logout waiting for the prize is cancelled when the database pauses (reason 6), and the character plays on;
    // the prize lands once the database answers.
    [Test]
    public void Grant_WhoseLogoutWaitsWhenTheDatabasePauses_CancelsTheLogout_ThenLands()
    {
        TestServer server = NewServer();
        long character = Create(server, "v13-cancel", "PrizeCancel");
        ConnectionId player = Enter(server, "v13-cancel", character);
        server.CrossIntoTheGrotto(player);
        server.RunsPersistence = false;
        CharacterSession mvp = Win(server, player);
        server.Tick();
        server.SendLogout(player, 1);
        server.Tick();
        bool isLoggingOut = mvp.IsLoggingOut;
        server.Transport.ClearSent();
        Pause();
        server.RunsPersistence = true;

        server.Tick(5);
        bool isCancelled = !mvp.IsLoggingOut;
        CommandRejectionReason answer = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .FirstOrDefault();
        Resume();
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return !mvp.HasInventoryWork;
            },
            "the grant settled after the pause");
        server.Tick(TestServer.TickRate);

        Assert.That((isLoggingOut, isCancelled), Is.EqualTo((true, true)), "the logout waited, then was cancelled");
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(server.SessionOf(player).State, Is.EqualTo(SessionState.InWorld), "the character plays on");
        Assert.That((StoredMantles(character), StoredGrants(character)), Is.EqualTo((1L, 1L)));
    }
}
}
