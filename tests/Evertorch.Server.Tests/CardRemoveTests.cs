using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Taking a card out at the Smith (Gameplay Systems §11.7; Persistence §5; Network Protocol §9, §11): the checks in
///     order, each refusal answered with its reason before anything is queued, the commit, the owner alone hearing its
///     bag's change, the log and the counters, and the store's refusals each keeping their reason.
/// </summary>
[TestFixture]
public sealed class CardRemoveTests
{
    private const string Smith = "npc.smith";
    private const string Quartermaster = "npc.quartermaster";
    private const string Sword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";
    private const string Gel = "item.material.slime_gel";
    private const string WeaponSlot = "Weapon";

    // Character 1 holds an Iron Sword carrying Keen then Bane, Keen cards, and the coins, enters, and stands 2 m west
    // of the NPC; the last argument changes the stored rows first.
    private static (TestServer Server, ConnectionId Player, Dictionary<string, long> Rows) Enter(
        long coins = 1000,
        string npc = Smith,
        float away = 2f,
        int keens = 1,
        Action<InMemoryGameStore, Dictionary<string, long>>? prepare = null)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        var rows = new Dictionary<string, long>();
        foreach ((string item, int quantity) in new[] { (Sword, 1), (Keen, keens) })
        {
            server.Store.GiveItems(1, item, 1, quantity, 5);
            rows[item] = server.Store.Stored(1).Items.Last().Id;
        }

        server.Store.Card(1, rows[Sword], Keen, Bane);
        prepare?.Invoke(server.Store, rows);
        server.Store.Edit(1, coins: coins);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf(npc);
        server.Place(player, keeper.Position.X + away, keeper.Position.Z);
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

    [TestCase("a shop's NPC", 1)]
    [TestCase("the Smith out of reach", 2)]
    [TestCase("an unknown row", 1)]
    [TestCase("a worn row", 3)]
    [TestCase("another card at the slot", 1)]
    [TestCase("a slot past the cards", 1)]
    [TestCase("too few coins", 10)]
    [TestCase("a full bag", 5)]
    public void Remove_ThatACheckRefuses_IsAnsweredWithItsReason_AndQueuesNothing(string refusal, int reason)
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = refusal switch
        {
            "a shop's NPC" => Enter(npc: Quartermaster),
            "the Smith out of reach" => Enter(away: 10f),
            "a worn row" => Enter(prepare: (store, held) => store.Wear(1, held[Sword], WeaponSlot)),
            "too few coins" => Enter(199),
            "a full bag" => Enter(keens: 999, prepare: (store, _) => store.GiveItems(1, Gel, 98, 1, 5)),
            _ => Enter()
        };
        EntityId npc = server.NpcOf(refusal == "a shop's NPC" ? Quartermaster : Smith).Id;
        (long row, byte slot, string card) = refusal switch
        {
            "an unknown row" => (999L, (byte)1, Keen),
            "another card at the slot" => (rows[Sword], (byte)1, Bane),
            "a slot past the cards" => (rows[Sword], (byte)3, Keen),
            _ => (rows[Sword], (byte)1, Keen)
        };

        server.SendRemoveCard(player, npc, row, slot, card, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo((CommandRejectionReason)reason), refusal);
        Assert.That(server.Store.CardCommits, Is.Empty, "nothing queued");
    }

    // The review's finding (session task T18): a full stack of the card leaves room in a row of its own, as the store
    // puts it there.
    [Test]
    public void Remove_BesideAFullStackOfTheCard_PutsItInARowOfItsOwn()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter(keens: 999);

        server.SendRemoveCard(player, server.NpcOf(Smith).Id, rows[Sword], 1, Keen, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            server.SessionOf(player).Character!.Inventory.Rows.Where(row => row.Item.Value == Keen)
                .Select(row => row.Quantity)
                .OrderBy(quantity => quantity),
            Is.EqualTo(new[] { 1u, 999u }));
    }

    [Test]
    public void Remove_TakesTheFee_MovesTheLaterCardsDown_AndMergesTheCardIntoItsStack()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        ConnectionId observer = server.EnterWorld(2);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendRemoveCard(player, server.NpcOf(Smith).Id, rows[Sword], 1, Keen, 1);
        CommandRejectionReason answer = Answer(server, player);

        InventoryChanged change = server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
            .Select(message => InventoryChanged.TryRead(message.Payload, out InventoryChanged? read) ? read! : null!)
            .Single();
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            change.Changes.Select(entry => (entry.InventoryItem, entry.Quantity, string.Join(",", entry.Cards))),
            Is.EquivalentTo(new[] { (rows[Sword], 1u, Bane), (rows[Keen], 2u, "") }));
        Assert.That(change.Coins, Is.EqualTo(800u), "the fee of 200");
        InMemoryGameStore.CardChangeEntry entry = server.Store.CardChanges.Values.Single().Single();
        Assert.That(
            (entry.Change, entry.Card, entry.Slot, entry.CardRow, entry.Fee),
            Is.EqualTo(("remove", Keen, 1, rows[Keen], 200L)));
        Assert.That(
            server.ItemActionLog.Entries.Where(log => log.EventId.Id == 1038)
                .Select(log => $"{log.Fields["Card"]} {log.Fields["Row"]} {log.Fields["Slot"]} {log.Fields["Fee"]}"),
            Is.EqualTo(new[] { $"{Keen} {rows[Sword]} 1 200" }));
        Assert.That(
            server.Transport.ControlSentTo(observer).Select(message => message.Opcode),
            Has.No.Member(MessageOpcode.InventoryChanged),
            "nothing is announced");
    }

    // The tick thread's copy allowed it, but the store found other cards: its refusal keeps its reason, 1.
    [Test]
    public void Remove_ThatTheStoreRefuses_IsAnsweredWithTheStoresReason()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        server.Store.Card(1, rows[Sword], Bane, Keen);

        server.SendRemoveCard(player, server.NpcOf(Smith).Id, rows[Sword], 1, Keen, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(server.Store.CardChanges, Is.Empty);
    }

    [Test]
    public void Remove_WhileAnotherItemActionIsInFlight_IsRefusedWithNine()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        EntityId smith = server.NpcOf(Smith).Id;

        server.SendRemoveCard(player, smith, rows[Sword], 1, Keen, 1);
        server.SendRemoveCard(player, smith, rows[Sword], 2, Bane, 2);
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

    // A lost answer settles through the ledger: the bag's change and the log as if it had come back.
    [Test]
    public void Remove_WhoseAnswerIsLost_SettlesThroughTheLedger()
    {
        (TestServer server, ConnectionId player, Dictionary<string, long> rows) = Enter();
        server.Store.AmbiguousCardFailures = 1;

        server.SendRemoveCard(player, server.NpcOf(Smith).Id, rows[Sword], 2, Bane, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            server.SessionOf(player).Character!.Inventory.Rows
                .Select(row => (row.Item.Value, row.Quantity, string.Join(",", row.Cards)))
                .OrderBy(row => row.Value, StringComparer.Ordinal),
            Is.EqualTo(new[] { (Keen, 1u, ""), (Bane, 1u, ""), (Sword, 1u, Keen) }));
        Assert.That(server.ItemActionLog.Entries.Count(log => log.EventId.Id == 1038), Is.EqualTo(1));
    }
}
}
