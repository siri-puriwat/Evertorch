using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's view of the market board (Gameplay Systems §11.5; Prototype Content §2): entries upserted in item
///     order, the listings taken whole as the end of a read, a refusal or a sale leaving it to be read again, and the
///     words.
/// </summary>
[TestFixture]
public sealed class ClientMarketTests
{
    private static readonly ItemDefinitionId Dust = new("item.material.gloom_dust");
    private static readonly ItemDefinitionId Sword = new("item.weapon.iron_sword");

    private static ClientMarket Read(params MarketListingEntry[] listings)
    {
        var market = new ClientMarket();
        market.BeginRead();
        market.OnBoard(
            new MarketBoard(new[] { new MarketBoardEntry(Sword, 120, 1), new MarketBoardEntry(Dust, 28, 6) }));
        market.OnMine(new MarketMine(listings));
        return market;
    }

    [Test]
    public void ACommitsRefusalOfOne_OrASale_LeavesItToBeReadAgain_AndARefusedReadEndsTheWaitUntilAskedAnew()
    {
        ClientMarket changed = Read();
        ClientMarket sold = Read(new MarketListingEntry(9, Dust, 6, 0, 0, 3600));
        ClientMarket poor = Read();
        var waiting = new ClientMarket();
        waiting.BeginRead();
        MarketEvent? heard = null;
        sold.NoticeReceived += notice => heard = notice;

        changed.OnRefused(MarketCommand.Buy, CommandRejectionReason.InvalidTarget);
        sold.OnNotice(new MarketEvent(MarketEventKind.Sold, 9, Dust, 4, 112));
        poor.OnRefused(MarketCommand.Buy, CommandRejectionReason.NotEnoughCoins);
        waiting.OnRefused(MarketCommand.Open, CommandRejectionReason.ServiceUnavailable);
        (bool, bool, bool) afterRefusal = (waiting.IsReading, waiting.IsCurrent, waiting.IsReadRefused);
        waiting.BeginRead();

        Assert.That((changed.IsCurrent, sold.IsCurrent, poor.IsCurrent), Is.EqualTo((false, false, true)));
        Assert.That(heard!.Coins, Is.EqualTo(112u), "the notice reaches the log");
        Assert.That(afterRefusal, Is.EqualTo((false, false, true)));
        Assert.That((waiting.IsReading, waiting.IsReadRefused), Is.EqualTo((true, false)), "asked anew");
    }

    [Test]
    public void ARead_UpsertsTheEntriesInItemOrder_AndEndsWithTheListings()
    {
        var market = new ClientMarket();
        market.BeginRead();
        market.OnBoard(
            new MarketBoard(new[] { new MarketBoardEntry(Sword, 120, 1), new MarketBoardEntry(Dust, 28, 6) }));
        (bool, bool) beforeTheListings = (market.IsReading, market.IsCurrent);
        market.OnMine(new MarketMine(new[] { new MarketListingEntry(9, Dust, 6, 4, 102, 3600) }));
        market.OnBoard(new MarketBoard(new[] { new MarketBoardEntry(Dust, 30, 0) }));

        Assert.That(beforeTheListings, Is.EqualTo((true, false)), "a read ends with the listings");
        Assert.That((market.IsReading, market.IsCurrent), Is.EqualTo((false, true)));
        Assert.That(
            market.Board.Select(entry => (entry.Item, entry.Price, entry.Stock)),
            Is.EqualTo(new[] { (Dust, 30u, 0u), (Sword, 120u, 1u) }),
            "a later entry replaces its item's");
        Assert.That(market.TryGetEntry(Sword, out MarketBoardEntry sword), Is.True);
        Assert.That(sword.Price, Is.EqualTo(120u));
        Assert.That(market.TryGetEntry(new ItemDefinitionId("item.material.slime_gel"), out _), Is.False);
        Assert.That(market.Listings.Single().Listing, Is.EqualTo(9L));
    }

    [Test]
    public void Notices_ReadAsThePlayerIsTold()
    {
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Listed, 9, Dust, 10, 2), null),
            Is.EqualTo("Listed item.material.gloom_dust x 10 for a fee of 2 coins."));
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Listed, 9, Sword, 1, 1), null),
            Is.EqualTo("Listed item.weapon.iron_sword for a fee of 1 coin."));
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Sold, 9, Dust, 4, 112), null),
            Is.EqualTo("Your item.material.gloom_dust x 4 sold for 112 coins."));
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Collected, 9, Dust, 6, 102), null),
            Is.EqualTo("Collected 102 coins and item.material.gloom_dust x 6."));
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Collected, 9, Dust, 0, 244), null),
            Is.EqualTo("Collected 244 coins."));
        Assert.That(
            MarketMessages.Describe(new MarketEvent(MarketEventKind.Collected, 9, Sword, 1, 0), null),
            Is.EqualTo("Collected item.weapon.iron_sword."));
    }

    [Test]
    public void Refusals_AreWordedPerCommand()
    {
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.Open, CommandRejectionReason.ServiceUnavailable),
            Is.EqualTo("The board cannot be read right now."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.List, CommandRejectionReason.RequirementNotMet),
            Is.EqualTo("You already have 8 listings."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.List, CommandRejectionReason.NotEnoughCoins),
            Is.EqualTo("You cannot pay the listing's fee."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.Buy, CommandRejectionReason.InvalidTarget),
            Is.EqualTo("The board changed: the price went up, or too few are left."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.Collect, CommandRejectionReason.CoinCapReached),
            Is.EqualTo("You cannot hold that many coins."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.Collect, CommandRejectionReason.InvalidTarget),
            Is.EqualTo("That listing is gone."));
        Assert.That(
            MarketMessages.DescribeRefusal(MarketCommand.Buy, CommandRejectionReason.OutOfRange),
            Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.OutOfRange)),
            "a reason without words of its own reads as any refusal");
    }

    [Test]
    public void Rows_AndPrompts_SayWhatAPressDoes()
    {
        Assert.That(
            MarketMessages.Entry(new MarketBoardEntry(Dust, 28, 6), null),
            Is.EqualTo("Buy item.material.gloom_dust: 28 coins, 6 for sale"));
        Assert.That(
            MarketMessages.Entry(new MarketBoardEntry(Dust, 1, 0), null),
            Is.EqualTo("item.material.gloom_dust: 1 coin, none for sale"));
        Assert.That(
            MarketMessages.BuyPrompt(Dust, 4, 28, null),
            Is.EqualTo("Press again to buy item.material.gloom_dust x 4 for 112 coins"));
        Assert.That(
            MarketMessages.Listing(new MarketListingEntry(9, Dust, 6, 4, 102, 0), 259_199, null),
            Is.EqualTo("Collect item.material.gloom_dust: 6 left, 4 sold, 102 coins waiting, 2d 23h left"));
        Assert.That(
            MarketMessages.Listing(new MarketListingEntry(9, Dust, 0, 10, 244, 0), 0, null),
            Is.EqualTo("Collect item.material.gloom_dust: 0 left, 10 sold, 244 coins waiting, expired"));
        Assert.That(
            MarketMessages.CollectPrompt(Dust, null),
            Is.EqualTo("Press again to end the item.material.gloom_dust listing early"));
        Assert.That(MarketMessages.BagRow(Sword, 1, null), Is.EqualTo("List item.weapon.iron_sword"));
        Assert.That(
            MarketMessages.ListPrompt(Dust, 4, MarketPricing.Fee(4, 28), null),
            Is.EqualTo("Press again to list item.material.gloom_dust x 4 for a fee of 1 coin"));
        Assert.That(
            new[] { 45u, 250u, 7_260u, 259_199u }.Select(MarketMessages.TimeLeft),
            Is.EqualTo(new[] { "45s", "4m 10s", "2h 1m", "2d 23h" }));
    }
}
}
