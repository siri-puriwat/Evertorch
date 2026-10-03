using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's model of the trade (Gameplay Systems §16; Prototype Content §2): a request waiting 30 s from when it
///     was heard, a trade opened with empty sides, each side as the server last sent it while the trade is open, and the
///     trade's words, the input gate's holders, and <c>/trade</c>.
/// </summary>
[TestFixture]
public sealed class ClientTradeTests
{
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");

    private static TradeSide Side(TradeSideOwner owner, uint coins, params TradeEntry[] entries)
    {
        return new TradeSide(owner, false, false, coins, entries);
    }

    // The server keeps a request whose answer came while an item change was going through; the client asks it again,
    // until it would have run out.
    [Test]
    public void ARequestAnsweredTooSoon_IsAskedAgain_UntilItWouldHaveRunOut()
    {
        var trade = new ClientTrade();
        trade.Apply(new TradeEvent(TradeEventKind.Requested, "Bobby"));
        trade.Stamp(100.0);

        trade.EndRequest();
        trade.ReopenRequest("Cora");
        string? other = trade.Requester;
        trade.ReopenRequest("Bobby");
        (string?, double) again = (trade.Requester, trade.RequestEndsAt);
        trade.ExpireRequest(130.0);

        Assert.That(other, Is.Null, "only the request answered");
        Assert.That(again, Is.EqualTo(("Bobby", 130.0)));
        Assert.That(trade.Requester, Is.Null);
    }

    [Test]
    public void ARequest_WaitsThirtySecondsFromWhenItWasHeard_AndAnOpeningEndsIt()
    {
        var trade = new ClientTrade();

        trade.Apply(new TradeEvent(TradeEventKind.Requested, "Bobby"));
        trade.Stamp(100.0);
        trade.ExpireRequest(129.9);
        string? waiting = trade.Requester;
        trade.ExpireRequest(130.0);

        Assert.That((waiting, trade.Requester), Is.EqualTo(("Bobby", (string?)null)));
        trade.Apply(new TradeEvent(TradeEventKind.Requested, "Cora"));
        trade.Apply(new TradeEvent(TradeEventKind.Opened, "Cora"));
        Assert.That((trade.Requester, trade.Partner, trade.IsOpen), Is.EqualTo(((string?)null, "Cora", true)));
    }

    [Test]
    public void Refusals_OfTradeCommands_SayWhatWasRefused()
    {
        Assert.That(
            TradeMessages.DescribeRefusal(TradeCommand.Request, "Bobby", CommandRejectionReason.OutOfRange),
            Is.EqualTo("Bobby is too far away to trade."));
        Assert.That(
            TradeMessages.DescribeRefusal(TradeCommand.Request, "Bobby", CommandRejectionReason.InvalidTarget),
            Is.EqualTo("Bobby is not online."));
        Assert.That(
            TradeMessages.DescribeRefusal(TradeCommand.Confirm, string.Empty, CommandRejectionReason.NotAllowedNow),
            Is.EqualTo("Both offers must be locked, and something offered, before you trade."));
        Assert.That(
            TradeMessages.DescribeRefusal(TradeCommand.Request, "Bobby", CommandRejectionReason.ItemActionInFlight),
            Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.ItemActionInFlight)));
        Assert.That(
            TradeMessages.DescribeRefusal(TradeCommand.Lock, string.Empty, CommandRejectionReason.NotAllowedNow),
            Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow)),
            "a second lock, or a cancel once both confirmed, while trading");
    }

    [Test]
    public void Sides_ApplyOnlyWhileOpen_AndAnEndClearsThem()
    {
        var trade = new ClientTrade();
        trade.Apply(Side(TradeSideOwner.Own, 5));
        Assert.That(trade.Own.Coins, Is.Zero, "no trade, no side");

        trade.Apply(new TradeEvent(TradeEventKind.Opened, "Bobby"));
        trade.Apply(Side(TradeSideOwner.Own, 50, new TradeEntry(7, Gel, 4)));
        trade.Apply(Side(TradeSideOwner.Partner, 20));

        Assert.That((trade.Own.Coins, trade.Theirs.Coins, trade.OfferedOf(7), trade.OfferedOf(8)),
            Is.EqualTo((50u, 20u, 4u, 0u)));
        foreach (TradeEventKind end in new[] { TradeEventKind.Completed, TradeEventKind.Cancelled })
        {
            trade.Apply(new TradeEvent(end, "Bobby"));
            Assert.That((trade.IsOpen, trade.Own.Coins, trade.OfferedOf(7)), Is.EqualTo((false, 0u, 0u)),
                end.ToString());
            trade.Apply(new TradeEvent(TradeEventKind.Opened, "Bobby"));
        }

        trade.Apply(new TradeEvent(TradeEventKind.Failed, "Bobby", CommandRejectionReason.InventoryFull));
        Assert.That(trade.IsOpen, Is.False);
    }

    [Test]
    public void SlashTrade_AsksToTradeWithTheNamedPlayer_OrSaysHow()
    {
        ChatRequest trade = ChatCommands.Parse("/trade Bobby", null, "Anna");

        Assert.That((trade.Trader, trade.Channel, trade.Refusal),
            Is.EqualTo(("Bobby", ChatChannel.None, (string?)null)));
        Assert.That(ChatCommands.Parse("/TRADE  Cora now", null, "Anna").Trader, Is.EqualTo("Cora"));
        Assert.That(ChatCommands.Parse("/trade", null, "Anna").Refusal, Is.EqualTo("Trade with /trade Name."));
        Assert.That(ChatCommands.Usage, Does.Contain("/trade Name"));
    }

    // Each field holds the gate on its own: the gate opens only once no field holds it (Prototype Content §4).
    [Test]
    public void TheInputGate_StaysShutUntilEveryFieldLetsGo()
    {
        var gate = new PlayerInputGate(null);
        object chat = new();
        object amount = new();

        gate.Shut(chat);
        gate.Shut(amount);
        gate.Shut(amount);
        gate.Open(chat);
        bool isShutWhileTheAmountHasFocus = gate.IsShut;
        gate.Open(amount);

        Assert.That((isShutWhileTheAmountHasFocus, gate.IsShut, gate.Holders), Is.EqualTo((true, false, 0)));
        TestDelegate again = () => gate.Open(amount);
        Assert.That(again, Throws.Nothing, "letting go twice is harmless");
    }

    [Test]
    public void TheWords_NameWhatHappened()
    {
        Assert.That(
            new[]
            {
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Requested, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Declined, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Expired, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Opened, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Cancelled, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Completed, "Anna")),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Failed, "Anna",
                    CommandRejectionReason.InventoryFull)),
                TradeMessages.Describe(new TradeEvent(TradeEventKind.Failed, "Anna",
                    CommandRejectionReason.CoinCapReached)),
                TradeMessages.Describe(
                    new TradeEvent(TradeEventKind.Unsaved, "Anna", CommandRejectionReason.ServiceUnavailable))
            },
            Is.EqualTo(new[]
            {
                "Anna wants to trade with you.",
                "Anna declined to trade.",
                "Anna did not answer.",
                "Trading with Anna.",
                "The trade was cancelled.",
                "Trade complete.",
                "The trade failed: Anna cannot hold it all.",
                "The trade failed: Anna cannot hold that many coins.",
                "The server cannot save right now. Confirm again."
            }));
        Assert.That(TradeMessages.Held, Is.EqualTo("You can't move while trading."));
        Assert.That(
            TradeMessages.Lines(
                Side(TradeSideOwner.Partner, 100, new TradeEntry(0, Gel, 4), new TradeEntry(0, Gel, 1, 7)),
                null),
            Is.EqualTo(new[] { $"{Gel.Value} x 4", $"+7 {Gel.Value}", "100 coins" }));
    }
}
}
