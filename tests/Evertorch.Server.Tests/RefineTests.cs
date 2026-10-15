using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Refining at the Smith (Gameplay Systems §11.6; Persistence §5; Network Protocol §9, §11): checks in order, one
///     draw once they pass, the commit of what it decided, the refiner alone hearing its bag's change and then the
///     outcome, the log and the counters, each refusal answered with its reason and drawing nothing, and the tables sent
///     after the Smith's services.
/// </summary>
[TestFixture]
public sealed class RefineTests
{
    private const string Smith = "npc.smith";
    private const string Quartermaster = "npc.quartermaster";
    private const string Sword = "item.weapon.training_sword";
    private const string IronSword = "item.weapon.iron_sword";
    private const string Keen = "item.card.forest_crawler";
    private const string Bane = "item.card.gloom_wisp";
    private const string Ember = "item.material.emberstone";
    private const string Gel = "item.material.slime_gel";
    private const string WeaponSlot = "Weapon";

    // Character 1 holds a training sword, or the item named, at the level, two Emberstones, and the coins, enters, and
    // stands 2 m west of the NPC.
    private static (TestServer Server, ConnectionId Player, long Sword) Enter(
        ScriptedRandom random,
        int level = 0,
        long coins = 100,
        int ore = 2,
        string npc = Smith,
        Action<InMemoryGameStore, long>? prepare = null,
        string item = Sword)
    {
        var server = new TestServer(withNpcs: true, refineRandom: random);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, item, 1, 1, 5);
        long sword = server.Store.Stored(1).Items.Single().Id;
        server.Store.Refine(1, sword, level);
        if (ore > 0)
        {
            server.Store.GiveItems(1, Ember, 1, ore, 5);
        }

        prepare?.Invoke(server.Store, sword);
        server.Store.Edit(1, coins: coins);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf(npc);
        server.Place(player, keeper.Position.X + 2f, keeper.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        return (server, player, sword);
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

    private static List<RefineOutcome> Outcomes(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.RefineOutcome)
            .Select(message => RefineOutcome.TryRead(message.Payload, out RefineOutcome? read) ? read! : null!)
            .ToList();
    }

    [TestCase("a shop's NPC", 1)]
    [TestCase("a worn row", 3)]
    [TestCase("a row at its table's last level", 3)]
    [TestCase("an item without a table", 3)]
    [TestCase("a level other than the row's", 1)]
    [TestCase("no ore", 13)]
    [TestCase("too few coins", 10)]
    public void Refine_ThatACheckRefuses_IsAnsweredWithItsReason_AndDrawsNothing(string refusal, int reason)
    {
        var random = new ScriptedRandom(0);
        (TestServer server, ConnectionId player, long sword) = refusal switch
        {
            "a shop's NPC" => Enter(random, npc: Quartermaster),
            "a worn row" => Enter(random, prepare: (store, row) => store.Wear(1, row, WeaponSlot)),
            "a row at its table's last level" => Enter(random, 10),
            "no ore" => Enter(random, ore: 0),
            "too few coins" => Enter(random, coins: 19),
            _ => Enter(random, prepare: (store, _) => store.GiveItems(1, Gel, 1, 1, 5))
        };
        long row = refusal == "an item without a table"
            ? server.SessionOf(player).Character!.Inventory.Rows.Single(entry => entry.Item.Value == Gel)
                .InventoryItem
            : sword;
        byte level = refusal switch
        {
            "a row at its table's last level" => 10,
            "a level other than the row's" => 3,
            _ => 0
        };
        NpcEntity npc = server.NpcOf(refusal == "a shop's NPC" ? Quartermaster : Smith);

        server.SendRefine(player, npc.Id, row, level, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo((CommandRejectionReason)reason), refusal);
        Assert.That(random.Calls, Is.Zero, "a refused command draws nothing");
        Assert.That(server.Store.RefineAttempts, Is.Empty);
        Assert.That(Outcomes(server, player), Is.Empty);
    }

    // Network Protocol §9: the Smith also takes cards out, and its fees follow its tables, every card by its ID; no
    // other NPC sends any.
    [Test]
    public void Entering_TellsTheSmithsCardFeesAfterItsTables_AndNoOtherNpcSendsAny()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick(5);
        EntityId smith = server.NpcOf(Smith).Id;
        InMemoryServerTransport.SentMessage[] sent = server.Transport.ControlSentTo(player).ToArray();
        int lastTable = Array.FindLastIndex(sent, message => message.Opcode == MessageOpcode.RefineTable);
        CardRemovalFees[] fees = sent.Where(message => message.Opcode == MessageOpcode.CardRemovalFees)
            .Select(message => CardRemovalFees.TryRead(message.Payload, out CardRemovalFees? read) ? read! : null!)
            .ToArray();
        NpcServices[] services = sent.Where(message => message.Opcode == MessageOpcode.NpcServices)
            .Select(message => NpcServices.TryRead(message.Payload, out NpcServices? read) ? read! : null!)
            .ToArray();

        Assert.That(sent[lastTable + 1].Opcode, Is.EqualTo(MessageOpcode.CardRemovalFees), "right after its tables");
        Assert.That(fees.Single().Npc, Is.EqualTo(smith));
        Assert.That(
            fees.Single().Entries.Select(entry => (entry.Card.Value, entry.Fee)),
            Is.EqualTo(
                new[]
                {
                    ("item.card.barrow_crawler", 600u), ("item.card.forest_crawler", 200u),
                    ("item.card.forest_crawler_stride", 200u), ("item.card.gloom_wisp", 600u),
                    ("item.card.gloom_wisp_veil", 600u), ("item.card.grotto_crawler", 600u),
                    ("item.card.grotto_crawler_bulwark", 600u), ("item.card.hollow_wisp", 600u),
                    ("item.card.hollow_wisp_poise", 600u), ("item.card.slime_monarch", 5000u),
                    ("item.card.spark_wisp", 200u), ("item.card.spark_wisp_insight", 200u),
                    ("item.card.training_slime", 200u)
                }));
        Assert.That(
            services.Where(service => service.RemovesCards).Select(service => service.Npc),
            Is.EqualTo(new[] { smith }));
    }

    // Network Protocol §9: a refiner's four tables follow its services, in the order of their IDs, each with its items;
    // no other NPC sends one.
    [Test]
    public void Entering_TellsTheSmithsTablesAfterItsServices_InIndexOrder()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick(5);
        EntityId smith = server.NpcOf(Smith).Id;
        InMemoryServerTransport.SentMessage[] sent = server.Transport.ControlSentTo(player).ToArray();
        int services = Array.FindIndex(sent, message => message.Opcode == MessageOpcode.NpcServices
            && NpcServices.TryRead(message.Payload, out NpcServices? read)
            && read!.Npc == smith);
        RefineTable[] tables = sent.Where(message => message.Opcode == MessageOpcode.RefineTable)
            .Select(message => RefineTable.TryRead(message.Payload, out RefineTable? read) ? read! : null!)
            .ToArray();

        Assert.That(
            sent.Skip(services + 1).Take(4).Select(message => message.Opcode),
            Is.All.EqualTo(MessageOpcode.RefineTable));
        Assert.That(tables.Select(table => table.Npc), Is.All.EqualTo(smith));
        Assert.That(
            tables.Select(table => (table.Index, table.Ore.Value, table.Steps.Count,
                string.Join(" ", table.Items.Select(item => item.Value)))),
            Is.EqualTo(
                new[]
                {
                    ((byte)0, "item.material.wardstone", 10,
                        "item.armor.cloth item.armor.leather item.armor.monarch_mantle"),
                    ((byte)1, "item.material.wardstone", 10,
                        "item.footwear.grotto_boots item.footwear.trail_boots item.garment.gloom_cape "
                        + "item.garment.travel_cape item.headgear.grotto_circlet item.headgear.trail_cap "
                        + "item.shield.carapace item.shield.wooden_buckler"),
                    ((byte)2, Ember, 10, "item.weapon.ash_staff item.weapon.iron_sword"),
                    ((byte)3, Ember, 10, "item.weapon.training_staff item.weapon.training_sword")
                }));
        Assert.That(
            tables[3].Steps.Select(step => (step.Chance, step.Coins)).Skip(6),
            Is.EqualTo(new[] { ((byte)100, 20u), ((byte)60, 20u), ((byte)40, 20u), ((byte)20, 20u) }));
    }

    [Test]
    public void Refine_FromZero_DrawsOnce_CommitsTheCostAndTheLevel_AndTellsTheRefinerItsBagThenTheOutcome()
    {
        var random = new ScriptedRandom(99);
        (TestServer server, ConnectionId player, long sword) = Enter(random);
        CharacterSession character = server.SessionOf(player).Character!;

        server.SendRefine(player, server.NpcOf(Smith).Id, sword, 0, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(random.Bounds, Is.EqualTo(new[] { 100 }), "one draw from 0 to 99, even at 100 %");
        Assert.That(
            character.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity, (int)row.RefineLevel)),
            Is.EquivalentTo(new[] { (Sword, 1u, 1), (Ember, 1u, 0) }));
        Assert.That(character.Inventory.Coins, Is.EqualTo(80u), "20 coins for +1");
        Assert.That(
            server.Transport.ControlSentTo(player)
                .Select(message => message.Opcode)
                .Where(opcode => opcode == MessageOpcode.InventoryChanged || opcode == MessageOpcode.RefineOutcome),
            Is.EqualTo(new[] { MessageOpcode.InventoryChanged, MessageOpcode.RefineOutcome }));
        RefineOutcome outcome = Outcomes(server, player).Single();
        Assert.That(
            (outcome.InventoryItem, outcome.Item.Value, outcome.LevelBefore, outcome.LevelAfter, outcome.Outcome),
            Is.EqualTo((sword, Sword, (byte)0, (byte)1, RefineOutcomeKind.Refined)));
        IReadOnlyDictionary<string, object?> fields =
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Id == 1034).Fields;
        Assert.That(
            (fields["Character"], fields["Item"], fields["LevelBefore"], fields["LevelAfter"], fields["Outcome"]),
            Is.EqualTo(((object?)1L, (object?)Sword, (object?)0, (object?)1, (object?)"refined")));
        Assert.That(server.Store.RefineAttempts.Values.Single().Outcome, Is.EqualTo(RefineOutcomeKind.Refined));
    }

    // At a safe level nothing can be destroyed, so a full bag refines a carded row.
    [Test]
    public void Refine_OfACardedRowAtASafeLevel_NeedsNoRoomForItsCards()
    {
        var random = new ScriptedRandom(0);
        (TestServer server, ConnectionId player, long sword) = Enter(
            random,
            0,
            1000,
            prepare: (store, row) =>
            {
                store.Card(1, row, Keen, Bane);
                store.GiveItems(1, Gel, 98, 1, 5);
            },
            item: IronSword);

        server.SendRefine(player, server.NpcOf(Smith).Id, sword, 0, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(Outcomes(server, player).Single().Outcome, Is.EqualTo(RefineOutcomeKind.Refined));
    }

    // A carded Iron Sword past its safe level, in a bag of 100 rows: once it and its row are gone, Keen takes the row it
    // leaves and Bane has none, so the refine is refused before its draw, whatever the draw would be (Gameplay Systems
    // §11.6, §11.7).
    [Test]
    public void Refine_OfACardedRowItCouldDestroy_WithoutRoomForItsCards_IsRefusedWithFive_AndDrawsNothing()
    {
        var random = new ScriptedRandom(0);
        (TestServer server, ConnectionId player, long sword) = Enter(
            random,
            7,
            1000,
            prepare: (store, row) =>
            {
                store.Card(1, row, Keen, Bane);
                store.GiveItems(1, Gel, 98, 1, 5);
            },
            item: IronSword);

        server.SendRefine(player, server.NpcOf(Smith).Id, sword, 7, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.InventoryFull));
        Assert.That(random.Calls, Is.Zero, "a refused command draws nothing");
    }

    // Past the training table's safe level a draw at or above the chance fails, and the table destroys the item.
    [Test]
    public void Refine_PastTheSafeLevel_WithAFailingDraw_DestroysTheRow_AndSaysSo()
    {
        var random = new ScriptedRandom(60);
        (TestServer server, ConnectionId player, long sword) = Enter(random, 7);
        CharacterSession character = server.SessionOf(player).Character!;

        server.SendRefine(player, server.NpcOf(Smith).Id, sword, 7, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(character.Inventory.Rows.Select(row => row.Item.Value), Is.EqualTo(new[] { Ember }));
        RefineOutcome outcome = Outcomes(server, player).Single();
        Assert.That(
            (outcome.LevelBefore, outcome.LevelAfter, outcome.Outcome),
            Is.EqualTo(((byte)7, (byte)0, RefineOutcomeKind.Destroyed)));
    }

    // Owner decision 13: a destroyed row's cards come back to the bag, each in a row of its own here, logged after the
    // refine by CardsReturned (Gameplay Systems §11.6).
    [Test]
    public void Refine_ThatDestroysACardedRow_ReturnsItsCardsToTheBag_AndLogsThem()
    {
        var random = new ScriptedRandom(99);
        (TestServer server, ConnectionId player, long sword) = Enter(
            random,
            7,
            1000,
            prepare: (store, row) => store.Card(1, row, Keen, Bane),
            item: IronSword);
        CharacterSession character = server.SessionOf(player).Character!;

        server.SendRefine(player, server.NpcOf(Smith).Id, sword, 7, 1);
        CommandRejectionReason answer = Answer(server, player);

        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(Outcomes(server, player).Single().Outcome, Is.EqualTo(RefineOutcomeKind.Destroyed));
        Assert.That(
            character.Inventory.Rows.Select(row => $"{row.Item.Value} x {row.Quantity}"),
            Is.EquivalentTo(new[] { $"{Ember} x 1", $"{Keen} x 1", $"{Bane} x 1" }));
        Assert.That(
            server.Store.CardChanges.Values.Single().Select(change => (change.Change, change.Card, change.Slot)),
            Is.EqualTo(new[] { ("return", Keen, 1), ("return", Bane, 2) }));
        Assert.That(
            server.ItemActionLog.Entries.Where(entry => entry.EventId.Id is 1034 or 1041)
                .Select(entry =>
                    entry.EventId.Id == 1041 ? $"{entry.Fields["Item"]}: {entry.Fields["Cards"]}" : "1034"),
            Is.EqualTo(new[] { "1034", $"{IronSword}: {Keen}, {Bane}" }));
    }

    // The review's finding (session task T17): a refine the writer cannot queue, its queue full, is refused with 6
    // before its draw, so a refused command draws nothing.
    [Test]
    public void Refine_ThatTheWriterCannotQueue_IsRefusedWithSix_AndDrawsNothing()
    {
        var random = new ScriptedRandom(0);
        var server = new TestServer(
            withNpcs: true,
            refineRandom: random,
            persistence: new PersistenceOptions { QueueCapacity = 1 });
        NpcEntity smith = server.NpcOf(Smith);
        var players = new List<(ConnectionId Player, long Sword)>();
        foreach (long character in new[] { 1L, 2L })
        {
            ConnectionId player = server.Connect();
            server.SignInWithCharacter(player, character);
            server.Store.GiveItems(character, Sword, 1, 1, 5);
            server.Store.GiveItems(character, Ember, 1, 1, 5);
            server.Store.Edit(character, coins: 100);
            server.SendEnterWorld(player, character);
            server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
            server.Place(player, smith.Position.X + 2f, smith.Position.Z + character - 1);
            players.Add((player,
                server.Store.Stored(character).Items.First(item => item.ItemDefinitionId == Sword).Id));
        }

        server.Tick(2);
        server.Transport.ClearSent();
        server.RunsPersistence = false;

        server.SendRefine(players[0].Player, smith.Id, players[0].Sword, 0, 1);
        server.SendRefine(players[1].Player, smith.Id, players[1].Sword, 0, 1);
        server.Tick();
        CommandRejectionReason second = server.Transport.ControlSentTo(players[1].Player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .Single();
        server.RunsPersistence = true;

        Assert.That(second, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(random.Calls, Is.EqualTo(1), "the queued refine alone drew");
    }
}
}
