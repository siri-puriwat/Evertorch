using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Evertorch.Rules;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The cards' durability on PostgreSQL 18 (Persistence §5, §11; Gameplay Systems §11.7): an insertion that empties
///     the card's row, a removal, and a refine break that returns its cards each happen once when every answer is lost,
///     settled from the ledger; an insertion through a paused database is held and settled at most once, and one while
///     the database is known down is refused with 6; the cards survive a restart; and a carded sale's ledger row names
///     the cards it lost. A carded item through a trade, a deposit, and a withdrawal is
///     <see cref="TradeAndStorageDurabilityTests" />'s.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CardDurabilityTests
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

    private const string Sword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";
    private const string Ember = "item.material.emberstone";
    private const string Smith = "npc.smith";
    private const string Quartermaster = "npc.quartermaster";
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

    private string Text(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    // The character's stored bag, "item quantity [cards]" by item, then the coins.
    private string Bag(long character)
    {
        return Text(
            "SELECT coalesce(string_agg(item_definition_id || ' ' || quantity "
            + "|| coalesce(' ' || (instance_data_json -> 'cards')::text, ''), ', ' ORDER BY item_definition_id), '') "
            + $"|| '; ' || (SELECT currency FROM characters WHERE id = {character}) FROM inventory_items "
            + $"WHERE character_id = {character}");
    }

    private long CardChanges(long character, string change)
    {
        return Scalar($"SELECT count(*) FROM card_changes WHERE character_id = {character} AND change = '{change}'");
    }

    private long LedgerRows(long character, string type)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = '{type}'");
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

    private static string Cards(TestServer server, ConnectionId connection, string item)
    {
        return string.Join(
            ",",
            CharacterOf(server, connection).Inventory.Rows.Single(row => row.Item.Value == item).Cards
                .Select(card => card.Value));
    }

    // A character stored holding an Iron Sword, carrying the cards given, the rows given, and 1,000 coins.
    private (long Character, string Identity) Carder(
        TestServer server,
        string tag,
        string swordCards,
        int level,
        params (string Item, int Quantity)[] rows)
    {
        string identity = $"v17-{tag}";
        long character = Create(server, identity, $"Card{tag}");
        string data = swordCards.Length == 0
            ? "NULL"
            : $"jsonb_build_object('cards', jsonb_build_array({swordCards}))";
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, instance_data_json, "
            + $"version) VALUES ({character}, '{Sword}', 1, {level}, {data}, 0)");
        foreach ((string item, int quantity) in rows)
        {
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"VALUES ({character}, '{item}', {quantity}, 0, 0)");
        }

        Execute($"UPDATE characters SET currency = 1000 WHERE id = {character}");
        return (character, identity);
    }

    // The database pauses with the insertion queued: it is held; once the database answers, the ledger settles it, and
    // it happens at most once.
    [Test]
    public void Insert_ThroughAPausedDatabase_IsHeld_ThenSettledAtMostOnce()
    {
        TestServer server = NewServer();
        (long character, string identity) = Carder(server, "Pause", string.Empty, 0, (Keen, 2));
        ConnectionId connection = Enter(server, identity, character);
        server.RunsPersistence = false;
        server.SendInsertCard(connection, RowOf(server, connection, Keen), RowOf(server, connection, Sword), 1);
        server.Tick();
        Pause();
        server.RunsPersistence = true;

        server.Tick(5);
        bool isHeld = CharacterOf(server, connection).Operation?.Kind == InventoryOperationKind.InsertCard;
        Resume();
        TickUntil(
            server,
            () =>
            {
                server.Persistence.Probe();
                return CharacterOf(server, connection).Operation == null;
            },
            "settled after the pause");

        bool isInserted = CardChanges(character, "insert") == 1;
        Assert.That(isHeld, Is.True, "held through the pause");
        Assert.That(CardChanges(character, "insert"), Is.LessThanOrEqualTo(1L), "at most once");
        Assert.That(
            Bag(character),
            Is.EqualTo(isInserted ? $"{Keen} 1, {Sword} 1 [\"{Keen}\"]; 1000" : $"{Keen} 2, {Sword} 1; 1000"));
    }

    [Test]
    public void Insert_WhileTheDatabaseIsKnownDown_IsRefusedWithSix()
    {
        TestServer server = NewServer();
        (long character, string identity) = Carder(server, "Down", string.Empty, 0, (Keen, 1));
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

        server.SendInsertCard(connection, RowOf(server, connection, Keen), RowOf(server, connection, Sword), 1);
        server.Tick();
        CommandRejectionReason refused = server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
        Resume();

        Assert.That(refused, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(Bag(character), Is.EqualTo(before), "nothing moved");
    }

    // The card's row holds one card, so the insertion empties it; every answer is lost, the writer replays the commit,
    // which the store answers from the ledger, and the server asks the ledger, which names both rows. It happens once,
    // and a new server on the same database loads the card in the sword.
    [Test]
    public void Insert_WhoseAnswersAreLost_EmptyingTheCardsRow_HappensOnce_AndSurvivesARestart()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store);
        (long character, string identity) = Carder(server, "Lost", string.Empty, 0, (Keen, 1));
        ConnectionId connection = Enter(server, identity, character);
        store.IsLosingAnswers = true;

        server.SendInsertCard(connection, RowOf(server, connection, Keen), RowOf(server, connection, Sword), 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "settled from the ledger");
        store.IsLosingAnswers = false;
        string[] held = CharacterOf(server, connection).Inventory.Rows.Select(row => row.Item.Value).ToArray();
        server.Disconnect(connection);
        server.Tick(2);
        TestServer restarted = NewServer();
        ConnectionId again = Enter(restarted, identity, character);

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Bag(character), Is.EqualTo($"{Sword} 1 [\"{Keen}\"]; 1000"), "the card's row gone, the card in");
        Assert.That(
            (LedgerRows(character, "card_insert"), CardChanges(character, "insert")),
            Is.EqualTo((1L, 1L)));
        Assert.That(held, Is.EqualTo(new[] { Sword }), "the server's copy agrees");
        Assert.That(Cards(restarted, again, Sword), Is.EqualTo(Keen), "loaded after a restart");
    }

    // A +7 Iron Sword carrying two cards meets a failing draw with the last ore: the sword and the ore go, and both cards
    // come back, each in a row of its own. Every answer is lost; the lookup names the returned rows, and it happens once.
    [Test]
    public void Refine_WhoseAnswersAreLost_DestroyingACardedRow_ReturnsItsCardsOnce()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store, new ScriptedRandom(99));
        (long character, string identity) = Carder(server, "Break", $"'{Keen}', '{Bane}'", 7, (Ember, 1));
        ConnectionId connection = Enter(server, identity, character);
        store.IsLosingAnswers = true;

        server.SendRefine(connection, server.NpcOf(Smith).Id, RowOf(server, connection, Sword), 7, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "settled from the ledger");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Bag(character), Is.EqualTo($"{Keen} 1, {Bane} 1; 900"), "the cards back, 100 coins spent once");
        Assert.That(
            (LedgerRows(character, "refine"), CardChanges(character, "return")),
            Is.EqualTo((1L, 2L)));
        Assert.That(
            CharacterOf(server, connection).Inventory.Rows.Select(row => row.Item.Value).OrderBy(item => item),
            Is.EqualTo(new[] { Keen, Bane }.OrderBy(item => item)),
            "the server's copy agrees");
    }

    // Every answer of a removal is lost: the fee is taken once, the card comes back once, and Bane moves down.
    [Test]
    public void Remove_WhoseAnswersAreLost_TakesTheFeeOnce_AndReturnsTheCardOnce()
    {
        var store = new LosingAnswersGameStore(NewStore());
        TestServer server = NewServer(store);
        (long character, string identity) = Carder(server, "Out", $"'{Keen}', '{Bane}'", 0);
        ConnectionId connection = Enter(server, identity, character);
        store.IsLosingAnswers = true;

        server.SendRemoveCard(connection, server.NpcOf(Smith).Id, RowOf(server, connection, Sword), 1, Keen, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "settled from the ledger");
        store.IsLosingAnswers = false;

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Bag(character), Is.EqualTo($"{Keen} 1, {Sword} 1 [\"{Bane}\"]; 800"));
        Assert.That(
            (LedgerRows(character, "card_remove"), CardChanges(character, "remove")),
            Is.EqualTo((1L, 1L)));
        Assert.That(
            (Cards(server, connection, Sword), CharacterOf(server, connection).Inventory.Coins),
            Is.EqualTo((Bane, 800L)),
            "the server's copy agrees");
    }

    // Selling a carded sword at the Quartermaster: the sale's ledger row names the cards it lost (Persistence §5).
    [Test]
    public void Sell_OfACardedRow_NamesTheLostCardsInItsLedgerRow()
    {
        TestServer server = NewServer();
        (long character, string identity) = Carder(server, "Sold", $"'{Keen}', '{Bane}'", 0);
        ConnectionId connection = Enter(server, identity, character, Quartermaster);

        server.SendSell(connection, server.NpcOf(Quartermaster).Id, RowOf(server, connection, Sword), 1, 1);
        server.Tick();
        TickUntil(server, () => CharacterOf(server, connection).Operation == null, "sold");

        Assert.That(Bag(character), Is.EqualTo("; 1060"));
        Assert.That(
            Text(
                "SELECT (metadata_json -> 'cards')::text FROM economy_ledger "
                + $"WHERE actor_character_id = {character} AND operation_type = 'sell'"),
            Is.EqualTo($"[\"{Keen}\", \"{Bane}\"]"));
    }
}
}
