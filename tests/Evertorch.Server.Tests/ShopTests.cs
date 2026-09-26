using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Buying from and selling to the Quartermaster (Gameplay Systems §6.1, §11.3; Persistence §5; Network Protocol §8,
///     §11): the checks in their order, each refusal answered with its reason and never scored, the reach of 3 m plus
///     the tolerance, and the commit before the owner is told.
/// </summary>
[TestFixture]
public sealed class ShopTests
{
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Potion = "item.consumable.minor_health";
    private const string Gel = "item.material.slime_gel";
    private const string Shell = "item.material.crawler_shell";
    private const string Sword = "item.weapon.training_sword";
    private const long Cap = 1_000_000_000;

    private sealed class Shop
    {
        public Shop(TestServer server, ConnectionId player, EntityId npc)
        {
            Server = server;
            Player = player;
            Npc = npc;
        }

        public TestServer Server { get; }

        public ConnectionId Player { get; }

        public EntityId Npc { get; }

        public CharacterSession Character => Server.SessionOf(Player).Character!;

        public long RowOf(string item)
        {
            return Server.Store.Stored(1).Items.First(stored => stored.ItemDefinitionId == item).Id;
        }

        // Answers the command sent last, once its tick and, if it passed, its commit have run.
        public CommandRejectionReason Answer()
        {
            Server.Tick();
            Server.TickUntil(() => Character.Operation == null);
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.CommandRejected)
                .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
                .DefaultIfEmpty(CommandRejectionReason.None)
                .Last();
        }

        public List<InventoryChanged> Changes()
        {
            return Server.Transport.ControlSentTo(Player)
                .Where(message => message.Opcode == MessageOpcode.InventoryChanged)
                .Select(message => InventoryChanged.TryRead(message.Payload, out InventoryChanged? read) ? read! : null)
                .Select(change => change!)
                .ToList();
        }
    }

    // Character 1 holds the coins and what prepare gives it, as earlier sessions would have left them, enters the
    // training ground, and stands the given distance east of the NPC, which it knows.
    private static Shop Enter(
        long coins,
        float distance = 2f,
        string npc = Quartermaster,
        Action<InMemoryGameStore>? prepare = null)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        prepare?.Invoke(server.Store);
        server.Store.Edit(1, coins: coins);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity trader = server.NpcOf(npc);
        server.Place(player, trader.Position.X + distance, trader.Position.Z);
        server.Tick(2);
        Assert.That(
            server.SessionOf(player).KnownEntities.Contains(trader.Id),
            Is.EqualTo(distance < 16f),
            "the NPC is known unless the player stands beyond its view");
        server.Transport.ClearSent();
        return new Shop(server, player, trader.Id);
    }

    private static Action<InMemoryGameStore> Holding(string item, int quantity)
    {
        return store => store.GiveItems(1, item, 1, quantity, 5);
    }

    private static void AssertNotScored(Shop shop)
    {
        Assert.That(shop.Server.SessionOf(shop.Player).Violations!.Value, Is.Zero, "a refusal is never scored");
    }

    // 3 m and the tolerance of 0.5 m, centre to centre on the ground (Gameplay Systems §6.1).
    [TestCase(3.4f, CommandRejectionReason.None)]
    [TestCase(3.6f, CommandRejectionReason.OutOfRange)]
    public void Buy_AtADistanceFromTheNpc_IsInReachUpToThreeAndAHalfMetres(
        float distance,
        CommandRejectionReason expected)
    {
        Shop shop = Enter(100, distance);

        shop.Server.SendBuy(shop.Player, shop.Npc, Potion, 1, 1);

        Assert.That(shop.Answer(), Is.EqualTo(expected));
        AssertNotScored(shop);
    }

    // Each case fails one check and passes the ones before it; where two fail, the earlier answers (Gameplay Systems
    // §6.1, §11.3).
    private static IEnumerable<TestCaseData> BuyRefusals()
    {
        yield return new TestCaseData(100L, 2f, Potion, 1u, "npc 999999", CommandRejectionReason.InvalidTarget)
            .SetName("Buy from an NPC the client does not know is 1");
        yield return new TestCaseData(100L, 2f, Potion, 1u, "the player", CommandRejectionReason.InvalidTarget)
            .SetName("Buy from a player is 1");
        yield return new TestCaseData(100L, 43.5f, Potion, 1u, "", CommandRejectionReason.InvalidTarget)
            .SetName("Buy from an NPC out of view, and so out of reach, is 1");
        yield return new TestCaseData(0L, 3.6f, Gel, 1u, "", CommandRejectionReason.OutOfRange)
            .SetName("Buy out of reach of an item not sold is 2");
        yield return new TestCaseData(0L, 2f, Gel, 1u, "", CommandRejectionReason.InvalidTarget)
            .SetName("Buy of an item not in the catalogue without coins is 1");
        yield return new TestCaseData(0L, 2f, Sword, 2u, "", CommandRejectionReason.NotAllowedNow)
            .SetName("Buy of two of an item with a stack limit of 1 without coins is 3");
        yield return new TestCaseData(39L, 2f, Potion, 2u, "", CommandRejectionReason.NotEnoughCoins)
            .SetName("Buy for one coin more than held is 10");
        yield return new TestCaseData(40L, 2f, Potion, 51u, "", CommandRejectionReason.NotAllowedNow)
            .SetName("Buy of more than the stack limit is 3");
    }

    [TestCaseSource(nameof(BuyRefusals))]
    public void Buy_ThatFailsACheck_IsRefusedWithItsReason_AndChangesNothing(
        long coins,
        float distance,
        string item,
        uint quantity,
        string npc,
        CommandRejectionReason expected)
    {
        Shop shop = Enter(coins, distance);
        EntityId target = npc switch
        {
            "npc 999999" => new EntityId(999999),
            "the player" => shop.Server.PlayerOf(shop.Player).Id,
            _ => shop.Npc
        };

        shop.Server.SendBuy(shop.Player, target, item, quantity, 1);

        Assert.That(shop.Answer(), Is.EqualTo(expected));
        Assert.That(shop.Server.Store.Stored(1).Coins, Is.EqualTo(coins), "no coins moved");
        Assert.That(shop.Server.Store.TradeCommits, Is.Empty, "nothing was committed");
        AssertNotScored(shop);
    }

    [Test]
    public void Buy_OfAPotion_TakesItsPriceOnce_AndTellsTheOwnerTheRowAndTheCoins()
    {
        Shop shop = Enter(100);

        shop.Server.SendBuy(shop.Player, shop.Npc, Potion, 2, 1);
        CommandRejectionReason answer = shop.Answer();

        InventoryChanged change = shop.Changes().Single();
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(change.Coins, Is.EqualTo(60u));
        Assert.That(change.Changes.Select(row => (row.Item.Value, row.Quantity)), Is.EqualTo(new[] { (Potion, 2u) }));
        Assert.That(shop.Server.Store.Stored(1).Coins, Is.EqualTo(60));
        Assert.That(shop.Character.Inventory.Coins, Is.EqualTo(60));
        IReadOnlyDictionary<string, object?> fields = shop.Server.ItemActionLog.Entries
            .Single(entry => entry.EventId.Name == "ItemBought")
            .Fields;
        Assert.That(
            (fields["Character"], fields["Quantity"], fields["Item"], fields["Coins"]),
            Is.EqualTo(((object?)1L, (object?)2, (object?)Potion, (object?)40L)));
    }

    [Test]
    public void Buy_PastTheStackLimit_OrWithNoFreeRow_IsReason5()
    {
        Shop fullStack = Enter(100, prepare: Holding(Potion, 50));
        Shop fullRows = Enter(100, prepare: store => store.GiveItems(1, Shell, 100, 1, 5));

        fullStack.Server.SendBuy(fullStack.Player, fullStack.Npc, Potion, 1, 1);
        fullRows.Server.SendBuy(fullRows.Player, fullRows.Npc, Sword, 1, 1);

        Assert.That(fullStack.Answer(), Is.EqualTo(CommandRejectionReason.InventoryFull));
        Assert.That(fullRows.Answer(), Is.EqualTo(CommandRejectionReason.InventoryFull));
    }

    // The server's copy of the coins said yes and the database said no: that copy is out of date, which nothing the
    // player sent could cause, so the answer is 1, as for the items of Milestone 6.
    [Test]
    public void Buy_ThatTheStoreRefuses_IsAnsweredOne_AndAuditedAsABuy()
    {
        Shop shop = Enter(100);
        shop.Server.Store.Edit(1, coins: 10);

        shop.Server.SendBuy(shop.Player, shop.Npc, Potion, 1, 1);

        Assert.That(shop.Answer(), Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(shop.Server.Store.TradeCommits, Has.Count.EqualTo(1));
        Assert.That(
            shop.Server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "CommandRefused").Fields["Command"],
            Is.EqualTo(InboundEventKind.Buy));
        AssertNotScored(shop);
    }

    [Test]
    public void Buy_WhileAnotherItemActionIsInFlight_IsReason9()
    {
        Shop shop = Enter(100, prepare: Holding(Potion, 3));
        shop.Server.RunsPersistence = false;

        shop.Server.SendUseItem(shop.Player, shop.RowOf(Potion), 1);
        shop.Server.SendBuy(shop.Player, shop.Npc, Potion, 1, 2);
        shop.Server.Tick();

        CommandRejected refused = shop.Server.Transport.ControlSentTo(shop.Player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .Single();
        Assert.That(
            (refused.CommandSequence, refused.Reason),
            Is.EqualTo((2u, CommandRejectionReason.ItemActionInFlight)));
        AssertNotScored(shop);
    }

    [Test]
    public void Commit_WhoseAnswerIsLost_IsSettledFromTheLedgerOnce()
    {
        Shop shop = Enter(100);
        shop.Server.Store.AmbiguousTradeFailures = 100;
        int ledger = shop.Server.Store.LedgerCount;

        shop.Server.SendBuy(shop.Player, shop.Npc, Potion, 1, 1);
        shop.Server.Tick(2);
        bool isUnsettled = shop.Character.Operation != null;
        shop.Server.Store.AmbiguousTradeFailures = 0;
        shop.Server.TickUntil(() => shop.Character.Operation == null);

        Assert.That(isUnsettled, Is.True, "no answer yet");
        Assert.That(shop.Server.Store.LedgerCount, Is.EqualTo(ledger + 1));
        Assert.That(shop.Server.Store.Stored(1).Coins, Is.EqualTo(80), "charged once");
        Assert.That(shop.Changes().Single().Coins, Is.EqualTo(80u));
        Assert.That(
            shop.Server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled")
                .Fields["Kind"],
            Is.EqualTo(InventoryOperationKind.Buy));
    }

    [Test]
    public void Sell_OfPartOfAStack_AddsWhatItFetches_AndTellsTheOwner()
    {
        Shop shop = Enter(0, prepare: Holding(Gel, 12));
        long gel = shop.RowOf(Gel);

        shop.Server.SendSell(shop.Player, shop.Npc, gel, 10, 1);
        CommandRejectionReason answer = shop.Answer();

        InventoryChanged change = shop.Changes().Single();
        Assert.That(answer, Is.EqualTo(CommandRejectionReason.None));
        Assert.That(change.Coins, Is.EqualTo(20u));
        Assert.That(change.Changes.Select(row => (row.InventoryItem, row.Quantity)), Is.EqualTo(new[] { (gel, 2u) }));
        IReadOnlyDictionary<string, object?> fields = shop.Server.ItemActionLog.Entries
            .Single(entry => entry.EventId.Name == "ItemSold")
            .Fields;
        Assert.That(
            (fields["Quantity"], fields["Item"], fields["Coins"]),
            Is.EqualTo(((object?)10, (object?)Gel, (object?)20L)));
    }

    [Test]
    public void Sell_ThatFailsACheck_IsRefusedWithItsReason_AndChangesNothing()
    {
        var answers = new List<(string, CommandRejectionReason)>();

        Shop warden = Enter(0, npc: GateWarden, prepare: Holding(Gel, 12));
        warden.Server.SendSell(warden.Player, warden.Npc, warden.RowOf(Gel), 1, 1);
        answers.Add(("an NPC without a shop", warden.Answer()));

        Shop notHeld = Enter(0, prepare: Holding(Gel, 12));
        notHeld.Server.SendSell(notHeld.Player, notHeld.Npc, 999999, 1, 1);
        answers.Add(("a row not the character's", notHeld.Answer()));

        Shop farAway = Enter(0, 3.6f, prepare: Holding(Gel, 12));
        farAway.Server.SendSell(farAway.Player, farAway.Npc, farAway.RowOf(Gel), 1, 1);
        answers.Add(("out of reach", farAway.Answer()));

        Shop worn = Enter(
            0,
            prepare: store =>
            {
                store.GiveItems(1, Sword, 1, 1, 5);
                store.Wear(1, store.Stored(1).Items.Single().Id, "Weapon");
            });
        long sword = worn.RowOf(Sword);
        worn.Server.SendSell(worn.Player, worn.Npc, sword, 1, 1);
        answers.Add(("a worn row", worn.Answer()));

        Shop tooMany = Enter(0, prepare: Holding(Gel, 12));
        tooMany.Server.SendSell(tooMany.Player, tooMany.Npc, tooMany.RowOf(Gel), 13, 1);
        answers.Add(("more than held", tooMany.Answer()));

        Shop capped = Enter(Cap - 10, prepare: Holding(Shell, 5));
        capped.Server.SendSell(capped.Player, capped.Npc, capped.RowOf(Shell), 5, 1);
        answers.Add(("past the coin cap", capped.Answer()));

        Assert.That(
            answers,
            Is.EqualTo(
                new[]
                {
                    ("an NPC without a shop", CommandRejectionReason.InvalidTarget),
                    ("a row not the character's", CommandRejectionReason.InvalidTarget),
                    ("out of reach", CommandRejectionReason.OutOfRange),
                    ("a worn row", CommandRejectionReason.NotAllowedNow),
                    ("more than held", CommandRejectionReason.NotAllowedNow),
                    ("past the coin cap", CommandRejectionReason.CoinCapReached)
                }));
        foreach (Shop shop in new[] { warden, notHeld, farAway, worn, tooMany, capped })
        {
            Assert.That(shop.Server.Store.TradeCommits, Is.Empty, "nothing was committed");
            AssertNotScored(shop);
        }

        Assert.That(
            capped.Server.AuditLogger.Entries.Single(entry => entry.EventId.Name == "CommandRefused").Fields["Command"],
            Is.EqualTo(InboundEventKind.Sell));
    }
}
}
