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
///     What the Ashen Quarry earns, kept once on PostgreSQL 18 (Milestone 21 line 6; Persistence §5, §7; Gameplay Systems
///     §11): both quarry quests, each turned in twice in a tick, pay once and are never taken again; the Furnace Heart's
///     ten Emberstones reach its most valuable player's bag once, or its feet when the bag is full; the Molten card,
///     inserted twice and taken out twice in a tick, moves once each way and survives a restart; and a hunt's progress
///     survives a reconnect.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class QuarryDurabilityTests
{
    private const string GateWarden = "npc.gate_warden";
    private const string Smith = "npc.smith";
    private const string QuarryHunt = "quest.quarry_hunt";
    private const string BossQuest = "quest.furnace_heart";
    private const string Emberstone = "item.material.emberstone";
    private const string Gel = "item.material.slime_gel";
    private const string Molten = "item.card.furnace_heart";
    private const string Leather = "item.armor.leather";
    private const string Cinder = "monster.cinder_crawler";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    private static readonly MapDefinitionId Quarry = new("map.ashen_quarry");

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

    // The store as the host builds it; the quarry, its monsters, and every map when a test fights there; the scripted
    // drops' first draw keeps every drop, and the AI is off.
    private TestServer NewServer(bool withQuarry = false)
    {
        return new TestServer(
            store: new PostgresGameStore(m_database.ConnectionString),
            withEveryMap: withQuarry,
            withQuarry: withQuarry,
            withMonsters: withQuarry,
            withMonsterAi: false,
            withNpcs: true,
            dropRandom: new ScriptedRandom(0));
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

    private long HeldOf(long character, string item)
    {
        return Scalar(
            "SELECT coalesce(sum(quantity), 0) FROM inventory_items "
            + $"WHERE character_id = {character} AND item_definition_id = '{item}'");
    }

    private long LedgerRows(long character, string operation)
    {
        return Scalar(
            $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
            + $"AND operation_type = '{operation}'");
    }

    private long CoinsOf(long character)
    {
        return Scalar($"SELECT currency FROM characters WHERE id = {character}");
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

    // Signs in as dev:<identity> and creates the character, without entering the world.
    private static long Create(TestServer server, string identity, string name)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        TickUntil(server, () => server.SessionOf(connection).Characters != null, "signed in");
        server.SendCreateCharacter(connection, name);
        TickUntil(
            server,
            () => server.SessionOf(connection).Characters!.Any(owned => owned.Name == name),
            $"{name} created");
        long character = server.SessionOf(connection).Characters!.Single(owned => owned.Name == name).Id;
        server.Disconnect(connection);
        server.Tick();
        return character;
    }

    private static ConnectionId Enter(TestServer server, string identity, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignIn(connection, $"dev:{identity}");
        TickUntil(server, () => server.SessionOf(connection).Characters != null, "signed in");
        server.SendEnterWorld(connection, character);
        TickUntil(server, () => server.SessionOf(connection).State == SessionState.InWorld, "in the world");
        return connection;
    }

    // Stands two metres east of the NPC, which it then knows.
    private static EntityId StandBy(TestServer server, ConnectionId player, string npc)
    {
        NpcEntity entity = server.NpcOf(npc);
        server.Place(player, entity.Position.X + 2f, entity.Position.Z);
        server.Tick(2);
        Assert.That(server.SessionOf(player).KnownEntities.Contains(entity.Id), Is.True, $"{npc} known");
        return entity.Id;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static long RowOf(TestServer server, ConnectionId player, string item)
    {
        return CharacterOf(server, player).Inventory.Rows.Single(row => row.Item.Value == item).InventoryItem;
    }

    private static void Settle(TestServer server, ConnectionId player)
    {
        TickUntil(server, () => CharacterOf(server, player).Operation == null, "the operation settled");
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read)
                ? (read.CommandSequence, read.Reason)
                : (0u, CommandRejectionReason.None))
            .ToArray();
    }

    private static string ProgressOf(TestServer server, ConnectionId player, string quest)
    {
        return CharacterOf(server, player).Quests.TryGet(new QuestDefinitionId(quest), out CharacterQuest? held)
            ? $"{(held!.IsCompleted ? "completed" : "active")} {held.Progress}"
            : "none";
    }

    private static MapInstance QuarryOf(TestServer server)
    {
        server.World.TryGetMap(Quarry, out MapInstance? map);
        return map!;
    }

    // The boss falls to the player, its most valuable: its ten Emberstones land in the bag once, under one ledger row,
    // and a restart loads them; with the bag's 100 rows full, they wait at the player's feet under the grant's ID.
    [TestCase(false)]
    [TestCase(true)]
    public void TheFurnaceHeartsTenEmberstones_ReachItsMostValuablePlayerOnce_OrItsFeetWhenTheBagIsFull(bool isBagFull)
    {
        TestServer server = NewServer(true);
        string identity = isBagFull ? "v21-full" : "v21-roomy";
        long character = Create(server, identity, isBagFull ? "QuarryFull" : "QuarryRoomy");
        if (isBagFull)
        {
            Execute(
                "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
                + $"SELECT {character}, '{Gel}', 1, 0, 0 FROM generate_series(1, {PickupSystem.MaxInventoryRows})");
        }

        ConnectionId player = Enter(server, identity, character);
        server.CrossIntoTheQuarry(player);
        PlayerEntity mvp = server.PlayerOf(player);
        MapInstance quarry = QuarryOf(server);
        MonsterEntity heart = quarry.Monsters.Single(monster => monster.Definition.IsBoss);
        heart.LogMvpDealt(mvp.Character, 500);

        server.Combat.Kill(quarry, heart, mvp, server.CurrentTick);
        Guid grant = CharacterOf(server, player).PendingGrants.Peek().OperationId;
        TickUntil(server, () => !CharacterOf(server, player).HasInventoryWork, "the grant settled");
        ItemDropEntity[] prize = quarry.ItemDrops.Where(drop => drop.DropId == grant).ToArray();

        if (isBagFull)
        {
            Assert.That(HeldOf(character, Emberstone), Is.Zero, "nothing in the full bag");
            Assert.That(
                prize.Select(drop => (drop.Item.Value, drop.Amount, drop.Position.X, drop.Position.Z)),
                Is.EqualTo(new[] { (Emberstone, 10u, mvp.Position.X, mvp.Position.Z) }),
                "ten at the player's feet, under the grant's ID");
        }
        else
        {
            Assert.That(prize, Is.Empty, "none on the ground");
            Assert.That(
                (HeldOf(character, Emberstone), LedgerRows(character, "boss_reward")),
                Is.EqualTo((10L, 1L)),
                "ten in the bag, once");
            TestServer restarted = NewServer(true);
            ConnectionId again = Enter(restarted, identity, character);
            Assert.That(
                CharacterOf(restarted, again).Inventory.Rows
                    .Where(row => row.Item.Value == Emberstone)
                    .Sum(row => row.Quantity),
                Is.EqualTo(10),
                "and so after a restart");
        }
    }

    // A Cinder Crawler the player hit falls: the hunt moves from 4 to 5, the player leaves, and on entering again the
    // hunt stands at 5, as the database holds it.
    [Test]
    public void AHuntsProgress_SurvivesAReconnect()
    {
        TestServer server = NewServer(true);
        long character = Create(server, "v21-hunt", "QuarryHunt");
        Execute(
            "INSERT INTO character_quests (character_id, quest_definition_id, state, progress, started_at, version) "
            + $"VALUES ({character}, '{QuarryHunt}', 'active', 4, now(), 0)");
        ConnectionId player = Enter(server, "v21-hunt", character);
        server.CrossIntoTheQuarry(player);
        MapInstance quarry = QuarryOf(server);
        MonsterEntity cinder = quarry.Monsters.First(monster => monster.Definition.Id.Value == Cinder);
        cinder.LogDamage(server.PlayerOf(player).Character, 30);

        server.Combat.Kill(quarry, cinder, null, server.CurrentTick);
        server.Tick();
        string before = ProgressOf(server, player, QuarryHunt);
        server.Disconnect(player);
        TickUntil(
            server,
            () => server.World.Maps.All(map => map.Players.All(entity => entity.Character.Value != character)),
            "out of the world");
        ConnectionId again = Enter(server, "v21-hunt", character);

        Assert.That(before, Is.EqualTo("active 5"));
        Assert.That(
            Scalar(
                "SELECT progress FROM character_quests "
                + $"WHERE character_id = {character} AND quest_definition_id = '{QuarryHunt}'"),
            Is.EqualTo(5));
        Assert.That(ProgressOf(server, again, QuarryHunt), Is.EqualTo("active 5"));
    }

    // Each quest ready, turned in twice in one tick, then turned in and asked for again: the second turn-in is refused
    // while the first is in flight, a completed quest is neither turned in nor taken again, and each pays its coins
    // once.
    [Test]
    public void BothQuarryQuests_TurnedInTwiceInATick_PayOnce_AndNeitherIsTakenAgain()
    {
        TestServer server = NewServer();
        long character = Create(server, "v21-quests", "QuarryQuests");
        Execute(
            "INSERT INTO character_quests (character_id, quest_definition_id, state, progress, started_at, version) "
            + $"VALUES ({character}, '{QuarryHunt}', 'active', 10, now(), 0), "
            + $"({character}, '{BossQuest}', 'active', 1, now(), 0)");
        ConnectionId player = Enter(server, "v21-quests", character);
        EntityId warden = StandBy(server, player, GateWarden);
        long coins = CoinsOf(character);
        server.Transport.ClearSent();

        uint sequence = 0;
        foreach (string quest in new[] { QuarryHunt, BossQuest })
        {
            server.SendCompleteQuest(player, warden, quest, ++sequence);
            server.SendCompleteQuest(player, warden, quest, ++sequence);
            server.Tick();
            Settle(server, player);
            server.SendCompleteQuest(player, warden, quest, ++sequence);
            server.SendAcceptQuest(player, warden, quest, ++sequence);
            server.Tick();
            Settle(server, player);
        }

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (3u, CommandRejectionReason.NotAllowedNow),
                    (4u, CommandRejectionReason.NotAllowedNow), (6u, CommandRejectionReason.ItemActionInFlight),
                    (7u, CommandRejectionReason.NotAllowedNow), (8u, CommandRejectionReason.NotAllowedNow)
                }),
            "each repeat refused while the first was in flight, and a completed quest neither turned in nor taken again");
        Assert.That(LedgerRows(character, "quest_reward"), Is.EqualTo(2), "one reward each");
        Assert.That(CoinsOf(character), Is.EqualTo(coins + 1600 + 2000), "1,600 and 2,000 coins, once each");
        Assert.That(
            Scalar(
                "SELECT count(*) FROM character_quests "
                + $"WHERE character_id = {character} AND state = 'completed'"),
            Is.EqualTo(2),
            "both completed");
    }

    // The Molten card goes into leather armor twice in a tick and comes out twice in a tick at the Smith: each second
    // command is refused while the first is in flight, the card moves once each way, its fee of 5,000 coins is taken
    // once, and a restart finds the card in the bag and the armor bare.
    [Test]
    public void TheMoltenCard_InsertedAndTakenOutTwiceInATick_MovesOnceEachWay_AndSurvivesARestart()
    {
        TestServer server = NewServer();
        long character = Create(server, "v21-card", "QuarryCard");
        Execute($"UPDATE characters SET currency = 10000 WHERE id = {character}");
        Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{Molten}', 1, 0, 0), ({character}, '{Leather}', 1, 0, 0)");
        ConnectionId player = Enter(server, "v21-card", character);
        EntityId smith = StandBy(server, player, Smith);
        long card = RowOf(server, player, Molten);
        long armor = RowOf(server, player, Leather);
        server.Transport.ClearSent();

        server.SendInsertCard(player, card, armor, 1);
        server.SendInsertCard(player, card, armor, 2);
        server.Tick();
        Settle(server, player);
        string inserted = string.Join(
            ",",
            CharacterOf(server, player).Inventory.Rows.Single(row => row.InventoryItem == armor).Cards
                .Select(held => held.Value));
        server.SendRemoveCard(player, smith, armor, 1, Molten, 3);
        server.SendRemoveCard(player, smith, armor, 1, Molten, 4);
        server.Tick();
        Settle(server, player);

        Assert.That(inserted, Is.EqualTo(Molten), "the armor held the card");
        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (4u, CommandRejectionReason.ItemActionInFlight)
                }));
        Assert.That(
            (LedgerRows(character, "card_insert"), LedgerRows(character, "card_remove")),
            Is.EqualTo((1L, 1L)),
            "once each way");
        Assert.That(CoinsOf(character), Is.EqualTo(10000 - 5000), "its fee once");
        Assert.That(HeldOf(character, Molten), Is.EqualTo(1), "the one card back in the bag");

        TestServer restarted = NewServer();
        ConnectionId again = Enter(restarted, "v21-card", character);
        Assert.That(
            CharacterOf(restarted, again).Inventory.Rows
                .Select(row =>
                    $"{row.Item.Value} {row.Quantity} [{string.Join(",", row.Cards.Select(held => held.Value))}]")
                .OrderBy(row => row, StringComparer.Ordinal),
            Is.EqualTo(new[] { $"{Leather} 1 []", $"{Molten} 1 []" }),
            "and so after a restart");
    }
}
}
