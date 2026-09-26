using System;
using System.Collections.Generic;
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
///     Retries cannot duplicate value on PostgreSQL 18 (verification line V4, Persistence §5): a sword, a row of its own,
///     picked up once under a duplicate and a race; an equip, an unequip, and a use whose answers are lost, each
///     settled from the ledger once. The server runs on the real store, whose commits happen before their answers are
///     lost.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ItemValueDurabilityTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Potion = "item.consumable.minor_health";

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
        using (var connection = new NpgsqlConnection(m_database.ConnectionString))
        using (var command = new NpgsqlCommand(sql, connection))
        {
            connection.Open();
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static ItemDropEntity DropAt(TestServer server, ConnectionId player, string item, uint amount)
    {
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(item),
            amount,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    private static List<InventoryChanged> Changes(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
            .Select(message =>
            {
                InventoryChanged.TryRead(message.Payload, out InventoryChanged? change);
                return change!;
            })
            .ToList();
    }

    private static CommandRejectionReason[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .ToArray();
    }

    // Ticks until the player's inventory operation, already begun, has settled.
    private static void Settle(TestServer server, ConnectionId player)
    {
        for (int tick = 0; tick < 200 && CharacterOf(server, player).Operation != null; tick++)
        {
            server.Tick();
        }

        Assert.That(CharacterOf(server, player).Operation, Is.Null, "the operation settled");
    }

    [Test]
    public void EquipUnequipAndUse_WhoseAnswersAreLost_AreEachSettledFromTheLedgerOnce()
    {
        var store = new LosingAnswersGameStore(new PostgresGameStore(m_database.ConnectionString));
        var server = new TestServer(store: store);
        ConnectionId player = server.EnterWorldAs("v4-lost", "LostAnswers");
        long character = CharacterOf(server, player).Character.Value;
        server.SendPickup(player, DropAt(server, player, Sword, 1).Id, 1);
        server.Tick();
        Settle(server, player);
        server.SendPickup(player, DropAt(server, player, Potion, 3).Id, 2);
        server.Tick();
        Settle(server, player);
        long sword = CharacterOf(server, player).Inventory.Rows.Single(row => row.Item.Value == Sword).InventoryItem;
        long potions = CharacterOf(server, player).Inventory.Rows.Single(row => row.Item.Value == Potion).InventoryItem;
        server.Transport.ClearSent();

        store.IsLosingAnswers = true;
        server.SendEquip(player, sword, 3);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        server.PlayerOf(player).CurrentHealth = 20;
        store.IsLosingAnswers = true;
        server.SendUseItem(player, potions, 4);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);
        store.IsLosingAnswers = true;
        server.SendUnequip(player, EquipmentSlot.Weapon, 5);
        server.Tick(3);
        store.IsLosingAnswers = false;
        Settle(server, player);

        Assert.That(store.LostAnswers, Is.GreaterThanOrEqualTo(3), "every commit lost at least one answer");
        Assert.That(
            Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
                + "AND operation_type IN ('equip', 'unequip', 'consume')"),
            Is.EqualTo(3),
            "one ledger row each");
        Assert.That(Scalar($"SELECT quantity FROM inventory_items WHERE id = {potions}"), Is.EqualTo(2));
        Assert.That(Scalar($"SELECT count(*) FROM equipment WHERE character_id = {character}"), Is.Zero);
        Assert.That(Changes(server, player), Has.Count.EqualTo(3), "each told once");
        Assert.That(
            server.PlayerOf(player).CurrentHealth,
            Is.InRange(50, 54),
            "the potion's 30 once, with at most two regeneration steps of 2");
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Sword_AskedForTwiceAndRacedForByAnotherPlayer_IsStoredOnce()
    {
        var server = new TestServer(store: new PostgresGameStore(m_database.ConnectionString));
        ConnectionId first = server.EnterWorldAs("v4-race-first", "RaceFirst");
        ConnectionId second = server.EnterWorldAs("v4-race-second", "RaceSecond");
        server.PlayerOf(second).Position = server.PlayerOf(first).Position;
        ItemDropEntity drop = DropAt(server, first, Sword, 1);

        server.SendPickup(first, drop.Id, 1);
        server.SendPickup(first, drop.Id, 2);
        server.SendPickup(second, drop.Id, 1);
        server.Tick();
        Settle(server, first);
        server.SendPickup(second, drop.Id, 2);
        server.Tick(2);

        Assert.That(
            Scalar(
                "SELECT count(*) FROM inventory_items i JOIN characters c ON c.id = i.character_id "
                + "WHERE c.name IN ('RaceFirst', 'RaceSecond') "
                + $"AND i.item_definition_id = '{Sword}' AND i.quantity = 1"),
            Is.EqualTo(1));
        Assert.That(Scalar($"SELECT count(*) FROM economy_ledger WHERE operation_id = '{drop.DropId}'"), Is.EqualTo(1));
        Assert.That(Rejections(server, first), Is.EqualTo(new[] { CommandRejectionReason.Busy }), "its own repeat");
        Assert.That(
            Rejections(server, second),
            Is.EqualTo(new[] { CommandRejectionReason.Busy, CommandRejectionReason.InvalidTarget }),
            "reserved, then gone");
    }
}
}
