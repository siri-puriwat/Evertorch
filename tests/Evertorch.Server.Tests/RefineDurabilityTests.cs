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
///     Refining's durability on PostgreSQL 18 (Persistence §11; Gameplay Systems §11.6): a refine while the database is
///     known down draws nothing and is refused with 6; one whose answers are lost, destroying the row and emptying the
///     ore, happens once and still tells its outcome; one through a paused database is held and settled at most once;
///     and a level refined at the Smith survives storage and a restart. The trade's part, a level through a trade, is
///     <see cref="TradeAndStorageDurabilityTests" />'s.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class RefineDurabilityTests
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

    private const string Sword = "item.weapon.training_sword";
    private const string Ember = "item.material.emberstone";
    private const string Smith = "npc.smith";
    private const string Storekeeper = "npc.storekeeper";
    private const int CommandTimeoutMs = 1000;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

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

    private TestServer NewServer(IGameStore? store = null, IRandomSource? random = null)
    {
        return new TestServer(
            persistence: new PersistenceOptions { CommandTimeoutMs = CommandTimeoutMs, RetryBaseDelayMs = 1 },
            store: store ?? NewStore(),
            withNpcs: true,
            refineRandom: random);
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

    // The character's stored bag, "item quantity +level" by item, then the coins.
    private string Bag(long character)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT coalesce(string_agg(item_definition_id || ' ' || quantity || ' +' || refine_level, ', ' "
            + "ORDER BY item_definition_id), '') "
            + $"|| '; ' || (SELECT currency FROM characters WHERE id = {character}) FROM inventory_items "
            + $"WHERE character_id = {character}",
            connection);
        return (string)command.ExecuteScalar()!;
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

    private static long Create(TestServer server, string identity, string name)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        TickUntil(server, () => server.SessionOf(connection).Characters != null, "signed in");

        // A container's first commands can outlast the short timeout these tests use, which refuses the creation;
        // asking again is what a player would do.
        for (int attempt = 0; attempt < 3 && !Has(server, connection, name); attempt++)
        {
            int answers = Created(server, connection);
            server.SendCreateCharacter(connection, name);
            TickUntil(server, () => Created(server, connection) > answers, $"{name} answered");
        }

        Assert.That(Has(server, connection, name), Is.True, $"{name} created");
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        server.Disconnect(connection);
        server.Tick();
        return character;
    }

    private static bool Has(TestServer server, ConnectionId connection, string name)
    {
        return server.SessionOf(connection).Characters!.Any(owned => owned.Name == name);
    }

    private static int Created(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Count(message => message.Opcode == MessageOpcode.CreateCharacterResult);
    }

    // Enters and stands two metres east of the NPC, which it knows.
    private static ConnectionId Enter(TestServer server, string identity, long character, string npc = Smith)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        TickUntil(server, () => server.SessionOf(connection).Characters != null, "signed in");
        server.SendEnterWorld(connection, character);
        TickUntil(server, () => server.SessionOf(connection).State == SessionState.InWorld, "in the world");
        NpcEntity keeper = server.NpcOf(npc);
        server.Place(connection, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);
        return connection;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId connection)
    {
        return server.SessionOf(connection).Character!;
    }

    private static long RowOf(TestServer server, ConnectionId connection, string item)
    {
        return CharacterOf(server, connection).Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    // A character stored holding a training sword at the level, the ore, and 100 coins.
    private (long Character, string Identity) Refiner(TestServer server, string tag, int level, int ore)
    {
        string identity = $"v16-{tag}";
        long character = Create(server, identity, $"Smith{tag}");
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) VALUES "
            + $"({character}, '{Sword}', 1, {level}, 0), ({character}, '{Ember}', {ore}, 0, 0)");
        Execute($"UPDATE characters SET currency = 100 WHERE id = {character}");
        return (character, identity);
    }

    private static CommandRejectionReason LastRefusal(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
    }

    private static RefineOutcome[] Outcomes(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.RefineOutcome)
            .Select(message => RefineOutcome.TryRead(message.Payload, out RefineOutcome? read) ? read! : null!)
            .ToArray();
    }

    private long LedgerRows(long character)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = 'refine'");
    }

    private long Attempts(long character)
    {
        return Scalar($"SELECT count(*) FROM refine_attempts WHERE character_id = {character}");
    }

    // Refined at the Smith from +6 to +7, the sword goes into storage and comes back, and a new server on the same
    // database loads it at +7.
    [Test]
    public void ALevelRefinedAtTheSmith_SurvivesStorage_AndARestart()
    {
        TestServer server = NewServer(random: new ScriptedRandom(99));
        (long character, string identity) = Refiner(server, "Kept", 6, 1);
        ConnectionId connection = Enter(server, identity, character);
        long sword = RowOf(server, connection, Sword);

        server.SendRefine(connection, server.NpcOf(Smith).Id, sword, 6, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "refined");
        NpcEntity keeper = server.NpcOf(Storekeeper);
        server.Place(connection, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);
        server.SendStorageDeposit(connection, keeper.Id, sword, 1, 2);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "deposited");
        long stored = Scalar($"SELECT id FROM storage_items WHERE item_definition_id = '{Sword}' AND refine_level = 7");
        server.SendStorageWithdraw(connection, keeper.Id, stored, 1, 3);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "withdrawn");
        server.Disconnect(connection);
        server.Tick(2);

        TestServer restarted = NewServer();
        ConnectionId again = Enter(restarted, identity, character);

        Assert.That(
            CharacterOf(restarted, again).Inventory.Rows.Single(row => row.Item.Value == Sword).RefineLevel,
            Is.EqualTo((byte)7));
        Assert.That(Bag(character), Is.EqualTo($"{Sword} 1 +7; 60"), "20 to refine, 20 to deposit");
    }

    // The database pauses with the refine queued: it is held, the refiner waiting; once the database answers, the
    // ledger settles it, and it happens at most once.
    [Test]
    public void Refine_ThroughAPausedDatabase_IsHeld_ThenSettledAtMostOnce()
    {
        TestServer server = NewServer(random: new ScriptedRandom(0));
        (long character, string identity) = Refiner(server, "Pause", 0, 2);
        ConnectionId connection = Enter(server, identity, character);
        server.RunsPersistence = false;
        server.SendRefine(connection, server.NpcOf(Smith).Id, RowOf(server, connection, Sword), 0, 1);
        server.Tick();
        Pause();
        server.RunsPersistence = true;

        server.Tick(5);
        bool isHeld = CharacterOf(server, connection).Operation?.Kind == InventoryOperationKind.Refine;
        Resume();
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return CharacterOf(server, connection).Operation == null;
            },
            "settled after the pause");

        bool isRefined = LedgerRows(character) == 1;
        Assert.That(isHeld, Is.True, "held through the pause");
        Assert.That(LedgerRows(character), Is.LessThanOrEqualTo(1L), "at most once");
        Assert.That(
            Bag(character),
            Is.EqualTo(isRefined ? $"{Ember} 1 +0, {Sword} 1 +1; 80" : $"{Ember} 2 +0, {Sword} 1 +0; 100"));
    }

    [Test]
    public void Refine_WhileTheDatabaseIsKnownDown_IsRefusedWithSix_AndDrawsNothing()
    {
        var random = new ScriptedRandom(0);
        TestServer server = NewServer(random: random);
        (long character, string identity) = Refiner(server, "Down", 0, 1);
        ConnectionId connection = Enter(server, identity, character);
        string before = Bag(character);
        Pause();
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return !server.Persistence.IsAvailable;
            },
            "the outage known");

        server.SendRefine(connection, server.NpcOf(Smith).Id, RowOf(server, connection, Sword), 0, 1);
        server.Tick();
        CommandRejectionReason refused = LastRefusal(server, connection);
        Resume();

        Assert.That(refused, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(random.Calls, Is.Zero, "no draw is spent on a command the database cannot take");
        Assert.That(Bag(character), Is.EqualTo(before), "nothing moved");
    }

    // The last ore and the sword at +7 meet a failing draw: both rows go. Every answer is lost; the writer replays the
    // commit, which the store answers from the ledger, and the server asks the ledger, which reports both rows under
    // their own items. It happens once, and the refiner still hears the outcome kept on the operation.
    [Test]
    public void Refine_WhoseAnswersAreLost_DestroyingTheRowAndEmptyingTheOre_HappensOnce_AndTellsTheOutcome()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store, new ScriptedRandom(60));
        (long character, string identity) = Refiner(server, "Lost", 7, 1);
        ConnectionId connection = Enter(server, identity, character);
        long sword = RowOf(server, connection, Sword);
        store.IsLosingAnswers = true;

        server.SendRefine(connection, server.NpcOf(Smith).Id, sword, 7, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "settled from the ledger");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Bag(character), Is.EqualTo("; 80"), "both rows gone, 20 coins spent once");
        Assert.That((LedgerRows(character), Attempts(character)), Is.EqualTo((1L, 1L)));
        Assert.That(CharacterOf(server, connection).Inventory.Rows, Is.Empty, "the server's copy agrees");
        RefineOutcome outcome = Outcomes(server, connection).Single();
        Assert.That(
            (outcome.InventoryItem, outcome.LevelBefore, outcome.Outcome),
            Is.EqualTo((sword, (byte)7, RefineOutcomeKind.Destroyed)));
    }
}
}
