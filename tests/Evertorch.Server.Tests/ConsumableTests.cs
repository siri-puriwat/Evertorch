using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Using a consumable (Gameplay Systems §11.2, Persistence §5, Network Protocol §8, §11): the checks in their order,
///     one inventory operation at a time, the commit before anyone is told, the effect capped at the maximums once it
///     returns, and a lost answer settled from the ledger.
/// </summary>
[TestFixture]
public sealed class ConsumableTests
{
    private const string Health = "item.consumable.minor_health";
    private const string Mana = "item.consumable.minor_mana";
    private const string Sword = "item.weapon.training_sword";
    private const string SlimeGel = "item.material.slime_gel";
    private const uint Revision = 5;

    private delegate bool TryRead<T>(byte[] payload, out T message);

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, TryRead<T> read)
    {
        var messages = new List<T>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == opcode && read(message.Payload, out T decoded))
            {
                messages.Add(decoded);
            }
        }

        return messages;
    }

    private static (uint Sequence, CommandRejectionReason Reason)[] Rejections(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.CommandRejected, (byte[] bytes, out CommandRejected read) =>
                CommandRejected.TryRead(bytes, out read))
            .Select(rejected => (rejected.CommandSequence, rejected.Reason))
            .ToArray();
    }

    private static List<InventoryChanged> Changes(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.InventoryChanged, (byte[] bytes, out InventoryChanged read) =>
        {
            bool isRead = InventoryChanged.TryRead(bytes, out InventoryChanged? message);
            read = message!;
            return isRead;
        });
    }

    private static List<CharacterHealth> Healths(TestServer server, ConnectionId player)
    {
        return Sent(server, player, MessageOpcode.CharacterHealth, (byte[] bytes, out CharacterHealth read) =>
            CharacterHealth.TryRead(bytes, out read));
    }

    private static (long, uint)[] Rows(InventoryChanged change)
    {
        return change.Changes.Select(row => (row.InventoryItem, row.Quantity)).ToArray();
    }

    private static long RowOf(TestServer server, long character, string item)
    {
        return server.Store.Stored(character).Items.Single(stored => stored.ItemDefinitionId == item).Id;
    }

    private static int QuantityOf(TestServer server, long character, long row)
    {
        return server.Store.Stored(character).Items.Where(stored => stored.Id == row).Sum(stored => stored.Quantity);
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    // Character 1 holds a stack of each item, as earlier pickups would have left them, and enters the world.
    private static ConnectionId EnterHolding(TestServer server, int quantity, params string[] items)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach (string item in items)
        {
            server.Store.GiveItems(1, item, 1, quantity, Revision);
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static void Settle(TestServer server, ConnectionId player)
    {
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
    }

    [Test]
    public void Commit_ThatNeverHappened_IsRefusedAsServiceUnavailable_AndRestoresNothing()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 3, Health);
        long potions = RowOf(server, 1, Health);
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 10;
        server.Transport.ClearSent();

        server.SendUseItem(player, potions, 1);
        server.Store.IsUnavailable = true;
        server.Tick(3);
        server.Store.IsUnavailable = false;
        Settle(server, player);

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { (1u, CommandRejectionReason.ServiceUnavailable) }));
        Assert.That(QuantityOf(server, 1, potions), Is.EqualTo(3));
        Assert.That(entity.CurrentHealth, Is.EqualTo(10));
    }

    [Test]
    public void Commit_WhoseAnswerIsLost_IsSettledFromTheLedgerOnce_AndRestoresOnce()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 3, Health);
        long potions = RowOf(server, 1, Health);
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 10;
        server.Store.AmbiguousConsumeFailures = 100;
        int ledger = server.Store.LedgerCount;
        server.Transport.ClearSent();

        server.SendUseItem(player, potions, 1);
        server.Tick(2);

        Assert.That(Changes(server, player), Is.Empty, "no answer yet, so nothing is told");
        Assert.That(entity.CurrentHealth, Is.EqualTo(10));
        Assert.That(server.Store.LedgerCount, Is.EqualTo(ledger + 1), "yet the unit was spent");
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled")
                .Fields["Kind"],
            Is.EqualTo(InventoryOperationKind.Consume));

        server.Store.AmbiguousConsumeFailures = 0;
        Settle(server, player);

        Assert.That(QuantityOf(server, 1, potions), Is.EqualTo(2));
        Assert.That(Rows(Changes(server, player).Single()), Is.EqualTo(new[] { (potions, 2u) }));
        Assert.That(entity.CurrentHealth, Is.EqualTo(40));
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Pickup_WhileAUseIsInFlight_IsReason9()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 2, Health);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            1,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendUseItem(player, RowOf(server, 1, Health), 1);
        server.SendPickup(player, drop.Id, 2);
        server.Tick();

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        Assert.That(drop.IsReserved, Is.False);
        server.RunsPersistence = true;
        Settle(server, player);
    }

    [Test]
    public void Refusals_AreAnsweredWithEachReasonInTheOrderOfTheChecks_AuditedAndNeverScored()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 2, Health, SlimeGel, Sword);
        server.EnterWorld(2);
        server.Store.GiveItems(2, Health, 1, 2, 1);
        long theirs = RowOf(server, 2, Health);
        long potions = RowOf(server, 1, Health);
        long gel = RowOf(server, 1, SlimeGel);
        long sword = RowOf(server, 1, Sword);
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendUseItem(player, potions, 1);
        server.SendUseItem(player, 999999, 2);
        server.SendEquip(player, sword, 3);
        server.Tick();
        server.RunsPersistence = true;
        Settle(server, player);
        server.RunsPersistence = false;
        server.SendEquip(player, sword, 4);
        server.SendUseItem(player, potions, 5);
        server.Tick();
        server.RunsPersistence = true;
        Settle(server, player);
        server.SendUseItem(player, theirs, 6);
        server.SendUseItem(player, gel, 7);
        server.SendUseItem(player, sword, 8);
        server.Tick();

        Assert.That(
            Rejections(server, player),
            Is.EqualTo(
                new[]
                {
                    (2u, CommandRejectionReason.ItemActionInFlight), (3u, CommandRejectionReason.ItemActionInFlight),
                    (5u, CommandRejectionReason.ItemActionInFlight), (6u, CommandRejectionReason.InvalidTarget),
                    (7u, CommandRejectionReason.NotAllowedNow), (8u, CommandRejectionReason.NotAllowedNow)
                }),
            "a use or an equip in flight before anything else; another's row; a material; a weapon");
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(
            server.AuditLogger.Entries.Count(entry => entry.EventId.Name == "CommandRefused"),
            Is.EqualTo(6));
        Assert.That(server.Store.ConsumeCommits, Has.Count.EqualTo(1));
        Assert.That(QuantityOf(server, 2, theirs), Is.EqualTo(2));
    }

    [Test]
    public void Use_IsCommittedBeforeAnyoneIsTold_ThenRestoresHp_AndTellsTheOwnerItsRowAndHealth()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 3, Health);
        long potions = RowOf(server, 1, Health);
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 20;
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendUseItem(player, potions, 1);
        server.Tick(2);

        Assert.That(Changes(server, player), Is.Empty, "no answer yet, so nothing is told");
        Assert.That(entity.CurrentHealth, Is.EqualTo(20), "and nothing is restored");

        server.RunsPersistence = true;
        Settle(server, player);

        InventoryChanged change = Changes(server, player).Single();
        Assert.That((change.PriorRevision, change.NewRevision), Is.EqualTo((Revision, Revision + 1)));
        Assert.That(Rows(change), Is.EqualTo(new[] { (potions, 2u) }));
        Assert.That(QuantityOf(server, 1, potions), Is.EqualTo(2));
        Assert.That(entity.CurrentHealth, Is.EqualTo(50), "20 and the potion's 30");
        CharacterHealth health = Healths(server, player).Single();
        Assert.That((health.Current, health.Maximum), Is.EqualTo((50u, 71u)));
        Assert.That(server.Store.ConsumeCommits, Has.Count.EqualTo(1));
        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Use_OfTheLastUnit_RemovesTheRow()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 1, Health);
        long potion = RowOf(server, 1, Health);
        server.Transport.ClearSent();

        server.SendUseItem(player, potion, 1);
        Settle(server, player);

        Assert.That(Rows(Changes(server, player).Single()), Is.EqualTo(new[] { (potion, 0u) }));
        Assert.That(CharacterOf(server, player).Inventory.Rows, Is.Empty);
        Assert.That(server.Store.Stored(1).Items, Is.Empty);
    }

    // The research note's vectors: HP 50 of 71 drinks to 71; SP 5 of 24 to 20.
    [Test]
    public void Use_RestoresAFlatAmount_CappedAtTheMaximum()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 2, Health, Mana);
        PlayerEntity entity = server.PlayerOf(player);
        entity.CurrentHealth = 50;
        entity.CurrentSpirit = 5;

        server.SendUseItem(player, RowOf(server, 1, Health), 1);
        Settle(server, player);
        server.SendUseItem(player, RowOf(server, 1, Mana), 2);
        Settle(server, player);

        Assert.That((entity.CurrentHealth, entity.CurrentSpirit), Is.EqualTo((71, 20)));
    }

    [Test]
    public void Use_WhoseCharacterDiesBeforeItsCommitReturns_SpendsTheUnit_AndRestoresNothing()
    {
        var server = new TestServer();
        ConnectionId player = EnterHolding(server, 3, Health);
        long potions = RowOf(server, 1, Health);
        server.RunsPersistence = false;
        server.SendUseItem(player, potions, 1);
        server.Tick();

        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(player), null, server.CurrentTick);
        server.RunsPersistence = true;
        Settle(server, player);

        Assert.That(QuantityOf(server, 1, potions), Is.EqualTo(2));
        Assert.That(server.PlayerOf(player).IsDead, Is.True);
        Assert.That(server.PlayerOf(player).CurrentHealth, Is.Zero);
    }
}
}
