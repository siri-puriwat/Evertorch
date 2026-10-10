using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The market board at the Broker (Gameplay Systems §11.5; Persistence §5; Network Protocol §9, §11): a read answered
///     with the board's entries and the reader's listings, listings, purchases, and collections checked in order and
///     committed before the owner hears its bag's change, its notice, and the item's entry and its listings again, a
///     sale's
///     notice to its seller alone, and each refusal answered with its reason and never scored.
/// </summary>
[TestFixture]
public sealed class MarketTests
{
    private const string Broker = "npc.broker";
    private const string Quartermaster = "npc.quartermaster";
    private const string Dust = "item.material.gloom_dust";
    private const string Sword = "item.weapon.iron_sword";
    private const string Gel = "item.material.slime_gel";
    private const long Seller = 1;
    private const long Buyer = 2;

    // Character 1 holds the coins and what prepare gives it, as earlier sessions would have left them, enters the
    // training ground, and stands the given distance west of the NPC, which it knows.
    private static ConnectionId Enter(
        TestServer server,
        long character,
        long coins,
        Action<InMemoryGameStore>? prepare = null,
        float distance = 2f,
        string npc = Broker)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, character);
        prepare?.Invoke(server.Store);
        server.Store.Edit(character, coins: coins);
        server.SendEnterWorld(player, character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity keeper = server.NpcOf(npc);
        server.Place(player, keeper.Position.X - distance, keeper.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        return player;
    }

    private static Action<InMemoryGameStore> Holding(long character, string item, int quantity)
    {
        return store => store.GiveItems(character, item, 1, quantity, 5);
    }

    private static EntityId BrokerOf(TestServer server)
    {
        return server.NpcOf(Broker).Id;
    }

    private static long RowOf(TestServer server, ConnectionId player, string item)
    {
        return server.SessionOf(player).Character!.Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    // Answers the command the player sent last, once its tick, its commit, and the reads after it have run.
    private static CommandRejectionReason Answer(TestServer server, ConnectionId player)
    {
        CharacterSession character = server.SessionOf(player).Character!;
        server.Tick();
        server.TickUntil(() => character.Operation == null
            && !character.IsReadingMarket
            && server.Persistence.PendingJobs == 0);
        return Rejections(server, player).Select(rejected => rejected.Reason)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Last();
    }

    private static List<CommandRejected> Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .ToList();
    }

    private static List<T> Sent<T>(TestServer server, ConnectionId player, MessageOpcode opcode, Read<T> read)
        where T : class
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload, out T? value) ? value! : null)
            .Select(value => value!)
            .ToList();
    }

    private delegate bool Read<T>(ReadOnlySpan<byte> payload, out T? value)
        where T : class;

    private static List<MarketBoard> Boards(TestServer server, ConnectionId player)
    {
        return Sent<MarketBoard>(server, player, MessageOpcode.MarketBoard, MarketBoard.TryRead);
    }

    private static List<MarketMine> Mines(TestServer server, ConnectionId player)
    {
        return Sent<MarketMine>(server, player, MessageOpcode.MarketMine, MarketMine.TryRead);
    }

    private static List<MarketEvent> Notices(TestServer server, ConnectionId player)
    {
        return Sent<MarketEvent>(server, player, MessageOpcode.MarketEvent, MarketEvent.TryRead);
    }

    private static List<InventoryChanged> Changes(TestServer server, ConnectionId player)
    {
        return Sent<InventoryChanged>(server, player, MessageOpcode.InventoryChanged, InventoryChanged.TryRead);
    }

    // The order the market's messages reached the player in.
    private static MessageOpcode[] Told(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Select(message => message.Opcode)
            .Where(opcode => opcode == MessageOpcode.InventoryChanged
                || opcode == MessageOpcode.MarketEvent
                || opcode == MessageOpcode.MarketBoard
                || opcode == MessageOpcode.MarketMine)
            .ToArray();
    }

    private static void AssertNotScored(TestServer server, params ConnectionId[] players)
    {
        foreach (ConnectionId player in players)
        {
            Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero, "a refusal is never scored");
        }
    }

    // The seller lists ten Gloom Dust, all it holds, at the base price of 28: the fee is 2.
    private static (TestServer Server, ConnectionId Seller, long Listing) Listed(long sellerCoins = 100)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId seller = Enter(server, Seller, sellerCoins, Holding(Seller, Dust, 10));
        server.SendMarketList(seller, BrokerOf(server), RowOf(server, seller, Dust), 10, 1);
        Assert.That(Answer(server, seller), Is.EqualTo(CommandRejectionReason.None), "listed");
        long listing = Notices(server, seller).Single().Listing;
        server.Transport.ClearSent();
        return (server, seller, listing);
    }

    private static IEnumerable<TestCaseData> OpenRefusals()
    {
        yield return new TestCaseData(2f, Quartermaster, CommandRejectionReason.InvalidTarget)
            .SetName("Open at an NPC that keeps no board is 1");
        yield return new TestCaseData(3.6f, Broker, CommandRejectionReason.OutOfRange)
            .SetName("Open beyond three and a half metres is 2");
    }

    [TestCaseSource(nameof(OpenRefusals))]
    public void Open_ThatCannotBe_IsRefused(float distance, string npc, CommandRejectionReason expected)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(server, Seller, 0, distance: distance, npc: npc);

        server.SendMarketOpen(player, server.NpcOf(npc).Id, 1);

        Assert.That(Answer(server, player), Is.EqualTo(expected));
        Assert.That(Boards(server, player).Concat<object>(Mines(server, player)), Is.Empty);
        AssertNotScored(server, player);
    }

    // Each case fails one check and passes the ones before it (Gameplay Systems §11.5).
    private static IEnumerable<TestCaseData> ListRefusals()
    {
        yield return new TestCaseData(100L, 2f, Quartermaster, "dust", 1u, CommandRejectionReason.InvalidTarget)
            .SetName("List at an NPC that keeps no board is 1");
        yield return new TestCaseData(0L, 3.6f, Broker, "dust", 1u, CommandRejectionReason.OutOfRange)
            .SetName("List out of reach without coins is 2");
        yield return new TestCaseData(0L, 2f, Broker, "none", 1u, CommandRejectionReason.InvalidTarget)
            .SetName("List of a row not held without coins is 1");
        yield return new TestCaseData(0L, 2f, Broker, "worn", 1u, CommandRejectionReason.NotAllowedNow)
            .SetName("List of a worn row without coins is 3");
        yield return new TestCaseData(0L, 2f, Broker, "refined", 1u, CommandRejectionReason.NotAllowedNow)
            .SetName("List of a refined row without coins is 3");
        yield return new TestCaseData(0L, 2f, Broker, "gel", 1u, CommandRejectionReason.NotAllowedNow)
            .SetName("List of an item the board does not take without coins is 3");
        yield return new TestCaseData(0L, 2f, Broker, "dust", 11u, CommandRejectionReason.NotAllowedNow)
            .SetName("List of more than the row holds without coins is 3");
        yield return new TestCaseData(0L, 2f, Broker, "dust", 10u, CommandRejectionReason.NotEnoughCoins)
            .SetName("List without a coin is 10");
    }

    [TestCaseSource(nameof(ListRefusals))]
    public void List_ThatCannotBe_IsRefusedWithTheFirstReason(
        long coins,
        float distance,
        string npc,
        string row,
        uint quantity,
        CommandRejectionReason expected)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(server, Seller, coins, store =>
        {
            store.GiveItems(Seller, Dust, 1, 10, 5);
            store.GiveItems(Seller, Gel, 1, 10, 5);
            store.GiveItems(Seller, Sword, 2, 1, 5);
            long[] swords = store.Stored(Seller).Items.Where(item => item.ItemDefinitionId == Sword)
                .Select(item => item.Id)
                .ToArray();
            store.Wear(Seller, swords[0], "Weapon");
            store.Refine(Seller, swords[1], 1);
        }, distance, npc);
        CharacterInventory bag = server.SessionOf(player).Character!.Inventory;
        long id = row switch
        {
            "dust" => RowOf(server, player, Dust),
            "gel" => RowOf(server, player, Gel),
            "worn" => bag.WornIn(EquipmentSlot.Weapon),
            "refined" => bag.Rows.Single(entry => entry.RefineLevel > 0).InventoryItem,
            _ => 999999
        };

        server.SendMarketList(player, server.NpcOf(npc).Id, id, quantity, 1);

        Assert.That(Answer(server, player), Is.EqualTo(expected));
        Assert.That(server.Store.MarketCommits, Is.Empty, "nothing reached the store");
        AssertNotScored(server, player);
    }

    // Each case fails one check and passes the ones before it (Gameplay Systems §11.5).
    private static IEnumerable<TestCaseData> BuyRefusals()
    {
        yield return new TestCaseData(500L, 2f, Quartermaster, Dust, 1u, 0, CommandRejectionReason.InvalidTarget)
            .SetName("Buy at an NPC that keeps no board is 1");
        yield return new TestCaseData(0L, 3.6f, Broker, Dust, 1u, 0, CommandRejectionReason.OutOfRange)
            .SetName("Buy out of reach without coins is 2");
        yield return new TestCaseData(0L, 2f, Broker, Gel, 1u, 0, CommandRejectionReason.InvalidTarget)
            .SetName("Buy of an item the board does not take without coins is 1");
        yield return new TestCaseData(0L, 2f, Broker, Sword, 2u, 0, CommandRejectionReason.NotAllowedNow)
            .SetName("Buy of two of an item that stacks to one without coins is 3");
        yield return new TestCaseData(111L, 2f, Broker, Dust, 4u, 0, CommandRejectionReason.NotEnoughCoins)
            .SetName("Buy of four at 28 with 111 coins is 10");
        yield return new TestCaseData(500L, 2f, Broker, Dust, 4u, 100, CommandRejectionReason.InventoryFull)
            .SetName("Buy into a bag of a hundred rows is 5");
    }

    [TestCaseSource(nameof(BuyRefusals))]
    public void Buy_ThatCannotBe_IsRefusedWithTheFirstReason(
        long coins,
        float distance,
        string npc,
        string item,
        uint quantity,
        int rows,
        CommandRejectionReason expected)
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(
            server,
            Buyer,
            coins,
            store => store.GiveItems(Buyer, Sword, rows, 1, 5),
            distance,
            npc);

        server.SendMarketBuy(player, server.NpcOf(npc).Id, item, quantity, 28, 1);

        Assert.That(Answer(server, player), Is.EqualTo(expected));
        Assert.That(server.Store.MarketCommits, Is.Empty, "nothing reached the store");
        AssertNotScored(server, player);
    }

    [Test]
    public void Buy_OfTheLastUnits_RaisesThePrice_AndASellerAwayHearsNothing()
    {
        (TestServer server, ConnectionId seller, _) = Listed();
        ConnectionId buyer = Enter(server, Buyer, 500);
        server.Disconnect(seller);
        server.TickUntil(() => !server.Sessions.TryGetCharacter(new CharacterId(Seller), out _));

        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 10, 28, 1);

        Assert.That(Answer(server, buyer), Is.EqualTo(CommandRejectionReason.None));
        Assert.That(
            Boards(server, buyer).Single().Entries.Select(entry => (entry.Item.Value, entry.Price, entry.Stock)),
            Is.EqualTo(new[] { (Dust, 30u, 0u) }),
            "a step of a tenth of 28, rounded down, up");
        Assert.That(server.Store.Stored(Buyer).Coins, Is.EqualTo(220L), "ten at 28");
        Assert.That(Notices(server, seller), Is.Empty);
    }

    [Test]
    public void Buy_TakesOtherAccountsUnits_PaysTheirListing_AndTellsItsSellerAlone()
    {
        (TestServer server, ConnectionId seller, long listing) = Listed();
        ConnectionId buyer = Enter(server, Buyer, 500);
        ConnectionId observer = Enter(server, 3, 0);

        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 4, 28, 1);

        Assert.That(Answer(server, buyer), Is.EqualTo(CommandRejectionReason.None));
        InventoryChanged bag = Changes(server, buyer).Single();
        Assert.That(
            (bag.Coins, bag.Changes.Single().Item.Value, bag.Changes.Single().Quantity),
            Is.EqualTo((388u, Dust, 4u)),
            "four at 28");
        Assert.That(Notices(server, buyer), Is.Empty, "a purchase's line is the shop's, read from the change");
        Assert.That(
            Boards(server, buyer).Single().Entries.Select(entry => (entry.Item.Value, entry.Price, entry.Stock)),
            Is.EqualTo(new[] { (Dust, 28u, 6u) }),
            "six left, and no rise before the last unit");
        Assert.That(Mines(server, buyer).Single().Entries, Is.Empty);
        Assert.That(
            Told(server, buyer),
            Is.EqualTo(new[] { MessageOpcode.InventoryChanged, MessageOpcode.MarketBoard, MessageOpcode.MarketMine }));
        MarketEvent sold = Notices(server, seller).Single();
        Assert.That(
            (sold.Kind, sold.Listing, sold.Item.Value, sold.Quantity, sold.Coins),
            Is.EqualTo((MarketEventKind.Sold, listing, Dust, 4u, 112u)));
        Assert.That(Told(server, seller), Is.EqualTo(new[] { MessageOpcode.MarketEvent }),
            "the seller hears the sale alone");
        Assert.That(Told(server, observer), Is.Empty, "an observer hears nothing");
        Assert.That(
            (server.Store.Stored(Seller).Coins, server.Store.Stored(Buyer).Coins),
            Is.EqualTo((98L, 388L)),
            "the coins wait on the listing");
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "MarketSold").Fields["Listings"],
            Is.EqualTo($"{listing}"));
        AssertNotScored(server, seller, buyer);
    }

    [Test]
    public void Buy_ThatTheCommitRefuses_IsOne_PastTheLimit_ForTooFewUnits_AndForItsOwnAccountsStock()
    {
        (TestServer server, ConnectionId seller, _) = Listed(500);
        ConnectionId buyer = Enter(server, Buyer, 500);

        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 4, 27, 1);
        CommandRejectionReason pastTheLimit = Answer(server, buyer);
        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 11, 28, 2);
        CommandRejectionReason tooFew = Answer(server, buyer);
        server.SendMarketBuy(seller, BrokerOf(server), Dust, 1, 28, 2);
        CommandRejectionReason itsOwn = Answer(server, seller);

        Assert.That(
            (pastTheLimit, tooFew, itsOwn),
            Is.EqualTo(
                (CommandRejectionReason.InvalidTarget, CommandRejectionReason.InvalidTarget,
                    CommandRejectionReason.InvalidTarget)));
        Assert.That(
            (server.Store.Stored(Seller).Coins, server.Store.Stored(Buyer).Coins),
            Is.EqualTo((498L, 500L)),
            "nothing bought");
        Assert.That(Boards(server, buyer).Concat(Boards(server, seller)), Is.Empty, "a refusal pushes no board");
        AssertNotScored(server, seller, buyer);
    }

    [Test]
    public void Buy_WhoseAnswerIsLost_IsSettledFromTheLedger_AndItsSellerHearsNothing()
    {
        (TestServer server, ConnectionId seller, _) = Listed();
        ConnectionId buyer = Enter(server, Buyer, 500);
        server.Store.AmbiguousMarketFailures = 100;

        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 4, 28, 1);

        Assert.That(Answer(server, buyer), Is.EqualTo(CommandRejectionReason.None));
        Assert.That(Changes(server, buyer).Single().Coins, Is.EqualTo(388u));
        Assert.That(
            Boards(server, buyer).SelectMany(board => board.Entries).Single(entry => entry.Item.Value == Dust).Stock,
            Is.EqualTo(6u),
            "the item read again");
        Assert.That(Notices(server, seller), Is.Empty, "a seller's notice after a lost answer is skipped");
    }

    [Test]
    public void Collect_PaysTheSalesLessTheTax_GivesBackTheUnitsLeft_AndEndsTheListing()
    {
        (TestServer server, ConnectionId seller, long listing) = Listed();
        ConnectionId buyer = Enter(server, Buyer, 500);
        server.SendMarketBuy(buyer, BrokerOf(server), Dust, 4, 28, 1);
        Assert.That(Answer(server, buyer), Is.EqualTo(CommandRejectionReason.None), "bought");
        server.Transport.ClearSent();

        server.SendMarketCollect(seller, BrokerOf(server), listing, 2);

        Assert.That(Answer(server, seller), Is.EqualTo(CommandRejectionReason.None));
        InventoryChanged bag = Changes(server, seller).Single();
        Assert.That(
            (bag.Coins, bag.Changes.Single().Item.Value, bag.Changes.Single().Quantity),
            Is.EqualTo((200u, Dust, 6u)),
            "98 and 112 less a tax of 10");
        MarketEvent collected = Notices(server, seller).Single();
        Assert.That(
            (collected.Kind, collected.Listing, collected.Item.Value, collected.Quantity, collected.Coins),
            Is.EqualTo((MarketEventKind.Collected, listing, Dust, 6u, 102u)));
        Assert.That(Mines(server, seller).Single().Entries, Is.Empty, "the listing ended");
        Assert.That(
            Told(server, seller),
            Is.EqualTo(
                new[]
                {
                    MessageOpcode.InventoryChanged, MessageOpcode.MarketEvent, MessageOpcode.MarketBoard,
                    MessageOpcode.MarketMine
                }));
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "MarketCollected").Fields["Coins"],
            Is.EqualTo(102L));
    }

    [Test]
    public void Collect_ThatTheCommitRefuses_IsOneElevenFive_OrSixForAnItemTheContentLost()
    {
        (TestServer server, ConnectionId seller, long listing) = Listed();
        ConnectionId other = Enter(server, Buyer, 500);
        long retired = server.Store.PutOnBoard(Seller, "item.material.retired", 3, server.Time.GetUtcNow().UtcDateTime,
            60_000);

        server.SendMarketCollect(other, BrokerOf(server), listing, 1);
        CommandRejectionReason another = Answer(server, other);
        server.SendMarketCollect(seller, BrokerOf(server), 999, 2);
        CommandRejectionReason gone = Answer(server, seller);
        server.SendMarketCollect(seller, BrokerOf(server), retired, 3);
        CommandRejectionReason lost = Answer(server, seller);
        server.Store.GiveItems(Seller, Sword, 100, 1, 9);
        server.SendMarketCollect(seller, BrokerOf(server), listing, 4);
        CommandRejectionReason full = Answer(server, seller);
        server.Store.Edit(Seller, coins: ContentLimits.MaxCurrency);
        server.Store.SellFromListing(listing, 10, 280);
        server.SendMarketCollect(seller, BrokerOf(server), listing, 5);
        CommandRejectionReason capped = Answer(server, seller);

        Assert.That(
            (another, gone, lost, full, capped),
            Is.EqualTo(
                (CommandRejectionReason.InvalidTarget, CommandRejectionReason.InvalidTarget,
                    CommandRejectionReason.ServiceUnavailable, CommandRejectionReason.InventoryFull,
                    CommandRejectionReason.CoinCapReached)));
        IReadOnlyDictionary<string, object?> mismatch =
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "MarketContentMismatch").Fields;
        Assert.That(mismatch["Definition"], Is.EqualTo("item.material.retired"));
        Assert.That(mismatch["Listing"], Is.EqualTo(retired));
        Assert.That(Notices(server, seller).Concat(Notices(server, other)), Is.Empty);
        AssertNotScored(server, seller, other);
    }

    [Test]
    public void List_OfPartOfAStack_PaysTheFee_ThenTellsTheBagTheListingTheEntryAndTheListings()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(server, Seller, 100, Holding(Seller, Dust, 12));
        long dust = RowOf(server, player, Dust);

        server.SendMarketList(player, BrokerOf(server), dust, 10, 1);

        Assert.That(Answer(server, player), Is.EqualTo(CommandRejectionReason.None));
        InventoryChanged bag = Changes(server, player).Single();
        Assert.That(
            (bag.Coins, bag.Changes.Single().InventoryItem, bag.Changes.Single().Quantity),
            Is.EqualTo((98u, dust, 2u)),
            "a fee of 1 % of 280");
        MarketEvent listed = Notices(server, player).Single();
        Assert.That(
            (listed.Kind, listed.Item.Value, listed.Quantity, listed.Coins),
            Is.EqualTo((MarketEventKind.Listed, Dust, 10u, 2u)));
        Assert.That(
            Boards(server, player).Single().Entries.Select(entry => (entry.Item.Value, entry.Price, entry.Stock)),
            Is.EqualTo(new[] { (Dust, 28u, 0u) }),
            "the one item's entry");
        Assert.That(
            Mines(server, player).Single().Entries.Select(entry => (entry.Listing, entry.UnitsLeft)),
            Is.EqualTo(new[] { (listed.Listing, 10u) }));
        Assert.That(
            Told(server, player),
            Is.EqualTo(
                new[]
                {
                    MessageOpcode.InventoryChanged, MessageOpcode.MarketEvent, MessageOpcode.MarketBoard,
                    MessageOpcode.MarketMine
                }));
        Assert.That(server.Store.Stored(Seller).Coins, Is.EqualTo(98L));
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "MarketListed").Fields["Coins"],
            Is.EqualTo(2L));
        AssertNotScored(server, player);
    }

    [Test]
    public void List_ThatTheCommitRefuses_IsThirteen_ForANinthListing_AndTen_ForAFeeTheCoinsCannotPay()
    {
        var full = new TestServer(withNpcs: true);
        ConnectionId lister = Enter(full, Seller, 100, store =>
        {
            store.GiveItems(Seller, Dust, 1, 10, 5);
            for (int index = 0; index < MarketTerms.MaxListings; index++)
            {
                store.PutOnBoard(Seller, Dust, 1, full.Time.GetUtcNow().UtcDateTime, 60_000);
            }
        });
        var poor = new TestServer(withNpcs: true);
        ConnectionId pauper = Enter(poor, Seller, 1, Holding(Seller, Dust, 100));

        full.SendMarketList(lister, BrokerOf(full), RowOf(full, lister, Dust), 1, 1);
        poor.SendMarketList(pauper, BrokerOf(poor), RowOf(poor, pauper, Dust), 100, 1);

        Assert.That(
            (Answer(full, lister), Answer(poor, pauper)),
            Is.EqualTo((CommandRejectionReason.RequirementNotMet, CommandRejectionReason.NotEnoughCoins)),
            "a fee of 28 for a hundred");
        Assert.That(
            (full.Store.Stored(Seller).Coins, poor.Store.Stored(Seller).Coins),
            Is.EqualTo((100L, 1L)),
            "nothing taken");
        Assert.That(Changes(full, lister).Concat(Changes(poor, pauper)), Is.Empty);
        AssertNotScored(full, lister);
        AssertNotScored(poor, pauper);
    }

    [Test]
    public void List_WhoseAnswerIsLost_IsSettledFromTheLedgerOnce_WithoutANotice_AndTheBoardTellsTheRest()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(server, Seller, 100, Holding(Seller, Dust, 12));
        server.Store.AmbiguousMarketFailures = 100;

        server.SendMarketList(player, BrokerOf(server), RowOf(server, player, Dust), 10, 1);
        server.SendMarketList(player, BrokerOf(server), RowOf(server, player, Dust), 2, 2);

        Assert.That(Answer(server, player), Is.EqualTo(CommandRejectionReason.ItemActionInFlight));
        Assert.That(
            Rejections(server, player).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ItemActionInFlight) }));
        Assert.That(server.Store.MarketCommits.Distinct().Count(), Is.EqualTo(1), "one operation, its retries repeats");
        Assert.That(
            (Changes(server, player).Single().Coins, server.Store.Stored(Seller).Coins),
            Is.EqualTo((98u, 98L)));
        Assert.That(Notices(server, player), Is.Empty, "the lookup names no listing or fee");
        Assert.That(Mines(server, player).Single().Entries.Single().UnitsLeft, Is.EqualTo(10u));
        Assert.That(
            server.ItemActionLog.Entries.Single(entry => entry.EventId.Name == "InventoryOperationUnsettled")
                .Fields["Kind"],
            Is.EqualTo(InventoryOperationKind.MarketList));
    }

    [Test]
    public void Open_ReadsEveryListableItem_ThenTheReadersListings_AndTwoAskedTogetherAreReadOnce()
    {
        (TestServer server, ConnectionId seller, long listing) = Listed();
        int reads = server.Store.MarketReads.Count;

        server.SendMarketOpen(seller, BrokerOf(server), 2);
        server.SendMarketOpen(seller, BrokerOf(server), 3);

        Assert.That(Answer(server, seller), Is.EqualTo(CommandRejectionReason.None));
        Assert.That(server.Store.MarketReads.Count, Is.EqualTo(reads + 1),
            "the second was answered by the first's read");
        Assert.That(
            Boards(server, seller).Single().Entries.Select(entry => (entry.Item.Value, entry.Price, entry.Stock)),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.leather", 90u, 0u), ("item.armor.monarch_mantle", 1000u, 0u), (Dust, 28u, 0u),
                    ("item.material.grotto_carapace", 24u, 0u), ("item.material.monarch_jelly", 80u, 0u),
                    ("item.weapon.ash_staff", 120u, 0u), (Sword, 120u, 0u)
                }),
            "every listable item at its price; its own listing is not stock it may buy");
        MarketListingEntry mine = Mines(server, seller).Single().Entries.Single();
        Assert.That(
            (mine.Listing, mine.Item.Value, mine.UnitsLeft, mine.UnitsSold, mine.CoinsWaiting, mine.SecondsLeft),
            Is.EqualTo((listing, Dust, 10u, 0u, 0u, 259_200u)));
        Assert.That(
            Told(server, seller),
            Is.EqualTo(new[] { MessageOpcode.MarketBoard, MessageOpcode.MarketMine }));
    }

    [Test]
    public void Open_WhileTheDatabaseIsDown_IsRefusedWithSix()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = Enter(server, Seller, 0);
        server.Store.IsUnavailable = true;

        server.SendMarketOpen(player, BrokerOf(server), 1);
        server.Tick(3);

        Assert.That(Answer(server, player), Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(Boards(server, player).Concat<object>(Mines(server, player)), Is.Empty);
    }
}
}
