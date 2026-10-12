using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Inserting a card (Gameplay Systems §11.7; Persistence §5; Network Protocol §9, §11): the checks in order, each
///     refusal answered with its reason before anything is queued, the commit, the owner alone hearing its bag's change,
///     the log and the counter, and the store's refusals each keeping their reason.
/// </summary>
[TestFixture]
public sealed class CardInsertTests
{
    private const string Sword = "item.weapon.iron_sword";
    private const string Cloth = "item.armor.cloth";
    private const string Keen = "item.card.forest_crawler";
    private const string Hardy = "item.card.training_slime";
    private const string Gel = "item.material.slime_gel";

    // Character 1 holds two Keen cards, an Iron Sword with two slots, cloth armor, and a gel, as earlier sessions left
    // them, and enters; the last argument changes the stored rows first.
    private static (TestServer Server, ConnectionId Player, Dictionary<string, long> Rows) Enter(
        Action<InMemoryGameStore, Dictionary<string, long>>? prepare = null)
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        var rows = new Dictionary<string, long>();
        foreach ((string item, int quantity) in new[] { (Keen, 2), (Sword, 1), (Cloth, 1), (Gel, 1), (Hardy, 1) })
        {
            server.Store.GiveItems(1, item, 1, quantity, 5);
            rows[item] = server.Store.Stored(1).Items.Last().Id;
        }

        prepare?.Invoke(server.Store, rows);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        server.Tick(2);
        server.Transport.ClearSent();
        return (server, player, rows);
    }

    private static CommandRejectionReason Answer(TestServer server, ConnectionId player)
    {
        CharacterSession character = server.SessionOf(player).Character!;
        server.Tick();
        server.TickUntil(() => character.Operation == null && server.Persistence.PendingJobs == 0);
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
    }

    [TestCase("an unknown card row", 1)]
    [TestCase("a row that is no card", 3)]
    [TestCase("an unknown item row", 1)]
    [TestCase("an item the card does not fit", 3)]
    [TestCase("one row named twice", 3)]
    [TestCase("a worn row", 3)]
    [TestCase("a row without a free slot", 3)]
    public void Insert_ThatACheckRefuses_IsAnsweredWithItsReason_AndQueuesNothing(string refusal, int reason)
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = refusal switch
        {
            "a worn row" => Enter((store, held) => store.Wear(1, held[Sword], "Weapon")),
            "a row without a free slot" => Enter((store, held) => store.Card(1, held[Sword], Keen, Keen)),
            _ => Enter()
        };
        (long card, long row) = refusal switch
        {
            "an unknown card row" => (999L, rows[Sword]),
            "a row that is no card" => (rows[Gel], rows[Sword]),
            "an unknown item row" => (rows[Keen], 999L),
            "an item the card does not fit" => (rows[Keen], rows[Cloth]),
            "one row named twice" => (rows[Keen], rows[Keen]),
            _ => (rows[Keen], rows[Sword])
        };

        server.SendInsertCard(player, card, row, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo((CommandRejectionReason)reason), refusal);
        Assert.That(server.Store.CardCommits, Is.Empty, "nothing queued");
    }

    [Test]
    public void Insert_CommitsTheCardIntoTheFirstFreeSlot_AndTellsTheOwnerItsBag_LogAndCount()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        ConnectionId observer = server.EnterWorld(2);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendInsertCard(player, rows[Keen], rows[Sword], 1);
        CommandRejectionReason answer = Answer(server, player);

        InventoryChanged change = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
            .Select(message => InventoryChanged.TryRead(message.Payload, out InventoryChanged? read) ? read! : null!)
            .Single();
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            change.Changes.Select(entry => (entry.InventoryItem, entry.Quantity, string.Join(",", entry.Cards))),
            Is.EquivalentTo(new[] { (rows[Keen], 1u, ""), (rows[Sword], 1u, Keen) }));
        Assert.That(
            server.SessionOf(player).Character!.Inventory.Rows.Single(row => row.InventoryItem == rows[Sword]).Cards
                .Select(card => card.Value),
            Is.EqualTo(new[] { Keen }));
        Assert.That(server.Store.CardChanges.Values.Single().Single().Change, Is.EqualTo("insert"));
        Assert.That(
            server.ItemActionLog.Entries.Where(entry => entry.EventId.Id == 1037)
                .Select(entry => $"{entry.Fields["Card"]} {entry.Fields["Row"]} {entry.Fields["Slot"]}"),
            Is.EqualTo(new[] { $"{Keen} {rows[Sword]} 1" }));
        Assert.That(
            server.Transport.ControlSentTo(observer).Select(message => message.Opcode),
            Has.No.Member(MessageOpcode.InventoryChanged),
            "nothing is announced");
    }

    // The tick thread's copy allowed it, but the store found the row full: its refusal keeps its reason, 3, and the
    // bag is read again (Gameplay Systems §11.7).
    [Test]
    public void Insert_ThatTheStoreRefuses_IsAnsweredWithTheStoresReason()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        server.Store.Card(1, rows[Sword], Hardy, Hardy);

        server.SendInsertCard(player, rows[Keen], rows[Sword], 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(server.Store.CardChanges, Is.Empty);
    }

    [Test]
    public void Insert_WhileAnotherItemActionIsInFlight_IsRefusedWithNine()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();

        server.SendInsertCard(player, rows[Keen], rows[Sword], 1);
        server.SendInsertCard(player, rows[Hardy], rows[Cloth], 2);
        server.Tick();
        server.TickUntil(() => server.Persistence.PendingJobs == 0
            && server.SessionOf(player).Character!.Operation == null);

        Assert.That(
            server.Transport.ControlSentTo(player)
                .Where(message => message.Opcode == MessageOpcode.CommandRejected)
                .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
                .Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        Assert.That(server.Store.CardCommits, Has.Count.EqualTo(1));
    }
}
}
