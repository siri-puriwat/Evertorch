using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Converters on PostgreSQL 18 (Milestone 18 verification; Gameplay Systems §9.1, §11.2; Persistence §5): a use
///     whose answers are lost commits once, settled from the ledger, and starts its status once; a player who dies
///     before the answer gets none; and a restart keeps no effect, since effects are never stored. Earthen armor through
///     a trade, storage, and a restart is <see cref="TradeAndStorageDurabilityTests" />'s.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ConverterDurabilityTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Earth = "item.consumable.earth_converter";

    private static readonly StatusDefinitionId EarthWeapon = new("status.earth_weapon");

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

    private long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(m_database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
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
        for (int tick = 0; tick < 200 && CharacterOf(server, player).Operation != null; tick++)
        {
            server.Tick();
        }

        Assert.That(CharacterOf(server, player).Operation, Is.Null, "the operation settled");
    }

    private static void PickUp(TestServer server, ConnectionId player, string item, uint amount, uint sequence)
    {
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.First(map => map.Players.Contains(server.PlayerOf(player))),
            new ItemDefinitionId(item),
            amount,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.SendPickup(player, drop.Id, sequence);
        server.Tick();
        Settle(server, player);
    }

    // Enters holding two Earth Converters with a training sword worn.
    private static ConnectionId EnterArmed(TestServer server, string identity, string name)
    {
        ConnectionId player = server.EnterWorldAs(identity, name);
        PickUp(server, player, Sword, 1, 1);
        PickUp(server, player, Earth, 2, 2);
        server.SendEquip(player, RowOf(server, player, Sword), 3);
        server.Tick();
        Settle(server, player);
        Assert.That(server.PlayerOf(player).Weapon, Is.Not.Null, "the sword worn");
        return player;
    }

    private static int StatusListsTold(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player).Count(message => message.Opcode == MessageOpcode.StatusEffects);
    }

    private long Consumes(long character)
    {
        return Scalar(
            "SELECT count(*) FROM economy_ledger "
            + $"WHERE actor_character_id = {character} AND operation_type = 'consume'");
    }

    // Every answer is lost, so the writer replays the commit and the server settles it from the ledger: one unit
    // spent, one ledger row, and one status started and told. A new server on the same database loads the character
    // with no effect and a Neutral swing, its sword still worn.
    [Test]
    public void Use_WhoseAnswersAreLost_CommitsOnce_StartsItsStatusOnce_AndARestartKeepsNoEffect()
    {
        var store = new LosingAnswersGameStore(new PostgresGameStore(m_database.ConnectionString));
        var server = new TestServer(store: store);
        ConnectionId player = EnterArmed(server, "v18-lost", "ConvertLost");
        long character = CharacterOf(server, player).Character.Value;
        long converters = RowOf(server, player, Earth);
        server.Transport.ClearSent();

        store.IsLosingAnswers = true;
        server.SendUseItem(player, converters, 4);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        PlayerEntity entity = server.PlayerOf(player);
        server.Disconnect(player);
        server.Tick(2);
        var restarted = new TestServer(store: new PostgresGameStore(m_database.ConnectionString));
        ConnectionId again = restarted.EnterWorldAs("v18-lost", "ConvertLost");

        Assert.That(store.LostAnswers, Is.GreaterThan(0), "answers were lost");
        Assert.That(Consumes(character), Is.EqualTo(1L), "one ledger row");
        Assert.That(Scalar($"SELECT quantity FROM inventory_items WHERE id = {converters}"), Is.EqualTo(1L));
        Assert.That(entity.StatusEffects.Select(effect => effect.Status), Is.EqualTo(new[] { EarthWeapon }));
        Assert.That(StatusListsTold(server, player), Is.EqualTo(1), "told once");
        Assert.That(restarted.PlayerOf(again).StatusEffects, Is.Empty, "no effect is stored");
        Assert.That(
            (restarted.PlayerOf(again).AttackElement, restarted.PlayerOf(again).Weapon != null),
            Is.EqualTo((Element.Neutral, true)));
    }

    // The use is queued, the player dies before its answer, and the commit settles after: the unit is spent, as the
    // command was accepted, but no status starts on the dead (Gameplay Systems §9.1).
    [Test]
    public void Use_WhosePlayerDiesBeforeTheAnswer_SpendsTheUnit_ButStartsNoStatus()
    {
        var server = new TestServer(
            store: new PostgresGameStore(m_database.ConnectionString),
            withMonsters: true,
            withMonsterAi: false);
        ConnectionId player = EnterArmed(server, "v18-dead", "ConvertDead");
        long character = CharacterOf(server, player).Character.Value;
        PlayerEntity entity = server.PlayerOf(player);
        MapInstance map = server.World.Maps.First(candidate => candidate.Players.Contains(entity));
        server.Transport.ClearSent();

        server.RunsPersistence = false;
        server.SendUseItem(player, RowOf(server, player, Earth), 4);
        server.Tick();
        server.Combat.Kill(map, entity, map.Monsters.First(), server.CurrentTick);
        server.RunsPersistence = true;
        Settle(server, player);

        Assert.That(entity.IsDead, Is.True);
        Assert.That(Consumes(character), Is.EqualTo(1L), "the unit spent once");
        Assert.That(entity.StatusEffects, Is.Empty, "no status on the dead");
        Assert.That(entity.AttackElement, Is.EqualTo(Element.Neutral));
        Assert.That(StatusListsTold(server, player), Is.Zero);
    }
}
}
