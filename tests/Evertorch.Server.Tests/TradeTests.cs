using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The trade on the server before its commit (Milestone 14 line 4; Gameplay Systems §5, §16; Network Protocol §9,
///     §11): requests with their expiry, replies, offers seen by both, lock and confirm, cancels, the busy state that
///     refuses attacks, skills, and item and NPC commands and holds both traders still, and the tick pass that ends a
///     trade whose traders part.
/// </summary>
[TestFixture]
public sealed class TradeTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string Nine = "Tester9";
    private const string Gel = "item.material.slime_gel";
    private const string Potion = "item.consumable.minor_health";

    private static TradeRig NewRig()
    {
        return new TradeRig();
    }

    [Test]
    public void ANewRequest_FromTheSameRequester_ReplacesItsOld()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);

        rig.Request(seven, Eight);
        rig.Request(seven, Nine);

        Assert.That(rig.Server.Trades.PendingRequests, Is.EqualTo(1));
        Assert.That(rig.RefusalOf(eight, rig.Reply(eight, Seven, true)),
            Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(rig.RefusalOf(nine, rig.Reply(nine, Seven, true)), Is.EqualTo(CommandRejectionReason.None));
    }

    [Test]
    public void Accept_OpensTheTradeForBoth_WithEmptySides()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);

        rig.Open(seven, eight, Seven, Eight);

        Assert.That(rig.Events(seven).Last(), Is.EqualTo((TradeEventKind.Opened, Eight, CommandRejectionReason.None)));
        Assert.That(rig.Events(eight).Last(), Is.EqualTo((TradeEventKind.Opened, Seven, CommandRejectionReason.None)));
        Assert.That(rig.Sides(seven).Select(side => (side.Owner, side.Entries.Count, side.Coins)),
            Is.EquivalentTo(new[] { (TradeSideOwner.Own, 0, 0u), (TradeSideOwner.Partner, 0, 0u) }));
        Assert.That(rig.Server.Trades.OpenTrades, Is.EqualTo(1));
        Assert.That(rig.Server.PlayerOf(seven).IsTrading && rig.Server.PlayerOf(eight).IsTrading, Is.True);
    }

    [Test]
    public void Cancel_EndsTheTradeForBoth_AndLetsThemMoveAgain()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Open(seven, eight, Seven, Eight);

        uint cancel = rig.Cancel(eight);
        uint nothing = rig.Cancel(eight);

        Assert.That((rig.RefusalOf(eight, cancel), rig.RefusalOf(eight, nothing)),
            Is.EqualTo((CommandRejectionReason.None, CommandRejectionReason.NotAllowedNow)));
        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Cancelled, Eight, CommandRejectionReason.None)));
        Assert.That(rig.Events(eight).Last(),
            Is.EqualTo((TradeEventKind.Cancelled, Seven, CommandRejectionReason.None)));
        Assert.That(rig.Server.PlayerOf(seven).IsTrading || rig.Server.PlayerOf(eight).IsTrading, Is.False);
        Assert.That(rig.Server.Trades.OpenTrades, Is.Zero);
    }

    [Test]
    public void LockAndConfirm_FollowTheirOrder_AndNothingChangesAfterALock()
    {
        TradeRig rig = NewRig();
        ConnectionId sevenAgain = rig.Enter(7, store => store.Edit(7, coins: 50));
        ConnectionId eight = rig.Enter(8);
        rig.Open(sevenAgain, eight, Seven, Eight);

        uint emptyConfirm = rig.Confirm(sevenAgain);
        rig.Offer(sevenAgain, 0, 10);
        uint early = rig.Confirm(sevenAgain);
        uint locked = rig.Lock(sevenAgain);
        uint again = rig.Lock(sevenAgain);
        uint afterLock = rig.Offer(sevenAgain, 0, 20);
        rig.Lock(eight);
        uint confirmed = rig.Confirm(sevenAgain);

        Assert.That(
            new[] { emptyConfirm, early, locked, again, afterLock, confirmed }
                .Select(sequence => rig.RefusalOf(sevenAgain, sequence)),
            Is.EqualTo(new[]
            {
                CommandRejectionReason.NotAllowedNow, CommandRejectionReason.NotAllowedNow,
                CommandRejectionReason.None, CommandRejectionReason.NotAllowedNow,
                CommandRejectionReason.NotAllowedNow, CommandRejectionReason.None
            }));
        TradeSide last = rig.Sides(eight).Last(side => side.Owner == TradeSideOwner.Partner);
        Assert.That((last.IsLocked, last.IsConfirmed, last.Coins), Is.EqualTo((true, true, 10u)));
    }

    [Test]
    public void Offers_ReachBoth_ThePartnerWithoutTheRowsIDs_AndThoseThatCannotBe_AreRefused()
    {
        TradeRig rig = NewRig();
        ConnectionId sevenAgain = rig.Enter(7, store =>
        {
            store.GiveItems(7, Gel, 1, 10, 0);
            store.Edit(7, coins: 50);
        });
        ConnectionId eight = rig.Enter(8);
        long gel = rig.Server.SessionOf(sevenAgain).Character!.Inventory.Rows.Single().InventoryItem;
        rig.Open(sevenAgain, eight, Seven, Eight);

        uint offered = rig.Offer(sevenAgain, gel, 4);
        uint coins = rig.Offer(sevenAgain, 0, 50);
        uint tooMany = rig.Offer(sevenAgain, gel, 11);
        uint tooRich = rig.Offer(sevenAgain, 0, 51);
        uint notMine = rig.Offer(eight, gel, 1);

        Assert.That(
            new[] { offered, coins, tooMany, tooRich }.Select(sequence => rig.RefusalOf(sevenAgain, sequence)),
            Is.EqualTo(new[]
            {
                CommandRejectionReason.None, CommandRejectionReason.None, CommandRejectionReason.NotAllowedNow,
                CommandRejectionReason.NotAllowedNow
            }));
        Assert.That(rig.RefusalOf(eight, notMine), Is.EqualTo(CommandRejectionReason.InvalidTarget));
        TradeSide mine = rig.Sides(sevenAgain).Last(side => side.Owner == TradeSideOwner.Own);
        TradeSide seen = rig.Sides(eight).Last(side => side.Owner == TradeSideOwner.Partner);
        Assert.That(
            mine.Entries.Select(entry => (entry.InventoryItem, entry.Item.Value, entry.Quantity)),
            Is.EqualTo(new[] { (gel, Gel, 4u) }));
        Assert.That(
            seen.Entries.Select(entry => (entry.InventoryItem, entry.Item.Value, entry.Quantity)),
            Is.EqualTo(new[] { (0L, Gel, 4u) }));
        Assert.That((mine.Coins, seen.Coins), Is.EqualTo((50u, 50u)));
    }

    [Test]
    public void Request_ThatWaitsThirtySeconds_Expires_AndTellsTheRequester()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Request(seven, Eight);

        rig.Server.Tick(TradeSystem.RequestLifetimeMs * TestServer.TickRate / 1000);

        Assert.That(rig.Events(seven),
            Is.EqualTo(new[] { (TradeEventKind.Expired, Eight, CommandRejectionReason.None) }));
        Assert.That(rig.RefusalOf(eight, rig.Reply(eight, Seven, true)),
            Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Request_ToAPlayerBeside_TellsThePartner_AndADeclineTellsTheRequester()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);

        uint request = rig.Request(seven, Eight);
        uint decline = rig.Reply(eight, Seven, false);

        Assert.That((rig.RefusalOf(seven, request), rig.RefusalOf(eight, decline)),
            Is.EqualTo((CommandRejectionReason.None, CommandRejectionReason.None)));
        Assert.That(rig.Events(eight),
            Is.EqualTo(new[] { (TradeEventKind.Requested, Seven, CommandRejectionReason.None) }));
        Assert.That(rig.Events(seven),
            Is.EqualTo(new[] { (TradeEventKind.Declined, Eight, CommandRejectionReason.None) }));
        Assert.That(rig.Server.Trades.PendingRequests, Is.Zero);
    }

    [Test]
    public void Requests_ThatCannotBe_AreRefused()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Server.Place(nine, 10f, 0f);

        Assert.That(rig.RefusalOf(seven, rig.Request(seven, "Nobody1")),
            Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(rig.RefusalOf(seven, rig.Request(seven, Seven)), Is.EqualTo(CommandRejectionReason.NotAllowedNow));
        Assert.That(rig.RefusalOf(seven, rig.Request(seven, Nine)), Is.EqualTo(CommandRejectionReason.OutOfRange));
        rig.Request(seven, Eight);
        rig.Server.Place(nine, 0.5f, 0f);
        Assert.That(rig.RefusalOf(nine, rig.Request(nine, Eight)), Is.EqualTo(CommandRejectionReason.NotAllowedNow),
            "the partner holds another request");
    }

    [Test]
    public void TheTickPass_EndsATrade_WhenATraderDies_LogsOut_OrLosesItsConnection()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        ConnectionId ten = rig.Enter(10);
        rig.Open(seven, eight, Seven, Eight);
        rig.Open(nine, ten, Nine, "Tester10");

        rig.Server.Combat.Kill(
            rig.Server.SessionOf(eight).Map!,
            rig.Server.PlayerOf(eight),
            null,
            rig.Server.CurrentTick);
        rig.Server.Disconnect(ten);
        rig.Server.Tick(2);

        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Cancelled, Eight, CommandRejectionReason.None)));
        Assert.That(rig.Events(nine).Last(),
            Is.EqualTo((TradeEventKind.Cancelled, "Tester10", CommandRejectionReason.None)));
        Assert.That(rig.Server.Trades.OpenTrades, Is.Zero);
    }

    [Test]
    public void TheTickPass_EndsATrade_WhenATradersConnectionIsReplaced()
    {
        TradeRig rig = NewRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Open(seven, eight, Seven, Eight);

        ConnectionId replacement = rig.Server.EnterWorld(8);
        rig.Server.Tick();

        Assert.That(replacement, Is.Not.EqualTo(eight));
        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Cancelled, Eight, CommandRejectionReason.None)));
        Assert.That(rig.Server.PlayerOf(replacement).IsTrading, Is.False, "the new connection is free");
    }

    [Test]
    public void WhileTrading_TheTradersAreHeldStill_AndRefuseAttacksSkillsAndItemCommands_ButMayChat()
    {
        TradeRig rig = NewRig();
        ConnectionId sevenAgain = rig.Enter(7, store => store.GiveItems(7, Potion, 1, 2, 0));
        ConnectionId eight = rig.Enter(8);
        long potion = rig.Server.SessionOf(sevenAgain).Character!.Inventory.Rows.Single().InventoryItem;
        rig.Open(sevenAgain, eight, Seven, Eight);
        WorldPosition before = rig.Server.PlayerOf(sevenAgain).Position;

        rig.Server.SendMove(sevenAgain, 1, 1f, 0f);
        rig.Server.Tick(5);
        uint use = rig.Next(sevenAgain);
        rig.Server.SendUseItem(sevenAgain, potion, use);
        uint attack = rig.Next(sevenAgain);
        rig.Server.SendAttack(sevenAgain, new EntityId(999), attack);
        uint chat = rig.Next(sevenAgain);
        rig.Server.SendChat(sevenAgain, ChatChannel.Nearby, string.Empty, "one moment", chat);
        rig.Server.Tick();

        Assert.That(rig.Server.PlayerOf(sevenAgain).Position, Is.EqualTo(before), "held still");
        Assert.That(
            (rig.RefusalOf(sevenAgain, use), rig.RefusalOf(sevenAgain, attack), rig.RefusalOf(sevenAgain, chat)),
            Is.EqualTo((CommandRejectionReason.NotAllowedNow, CommandRejectionReason.NotAllowedNow,
                CommandRejectionReason.None)));
    }
}
}
