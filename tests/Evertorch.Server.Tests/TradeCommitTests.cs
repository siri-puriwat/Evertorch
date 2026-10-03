using System.Linq;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The trade's commit on the server (Milestone 14 line 4; Gameplay Systems §16; Network Protocol §9; System
///     Architecture §8): started by the tick pass once both confirmed, checked first, committed once for both traders,
///     each settled by a fresh inventory and then the completion, a refusal and an outage ending it with nothing moved,
///     a lost answer settled from the store, and one trader's fault never leaving the other stuck.
/// </summary>
[TestFixture]
public sealed class TradeCommitTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.training_sword";

    // Seven holds ten gel and 50 coins, eight a training sword; both enter beside each other and open a trade.
    private static (TradeRig Rig, ConnectionId Seven, ConnectionId Eight) Opened(TestServer? server = null)
    {
        var rig = new TradeRig(server);
        ConnectionId seven = rig.Enter(7, store =>
        {
            store.GiveItems(7, Gel, 1, 10, 0);
            store.Edit(7, coins: 50);
        });
        ConnectionId eight = rig.Enter(8, store => store.Edit(8, item: Sword));
        rig.Open(seven, eight, Seven, Eight);
        return (rig, seven, eight);
    }

    private static long RowOf(TradeRig rig, ConnectionId connection, string item)
    {
        return rig.Server.SessionOf(connection).Character!.Inventory.Rows.First(row => row.Item.Value == item)
            .InventoryItem;
    }

    // Seven offers four gel and its 50 coins, eight its sword; both lock and confirm.
    private static void Agree(TradeRig rig, ConnectionId seven, ConnectionId eight)
    {
        rig.Offer(seven, RowOf(rig, seven, Gel), 4);
        rig.Offer(seven, 0, 50);
        rig.Offer(eight, RowOf(rig, eight, Sword), 1);
        rig.Lock(seven);
        rig.Lock(eight);
        rig.Confirm(seven);
        rig.Confirm(eight);
    }

    private static string Bag(TestServer server, long character)
    {
        StoredCharacter stored = server.Store.Stored(character);
        return string.Join(", ", stored.Items.Select(item => $"{item.ItemDefinitionId} {item.Quantity}")) +
            $"; {stored.Coins}";
    }

    [Test]
    public void ACommitTheOutageCutShort_ThatNeverLanded_EndsTheTradeWithNothingMoved()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened(new TestServer(
            persistence: new PersistenceOptions
                { MaxRetries = 0, RetryBaseDelayMs = 1 }));
        Agree(rig, seven, eight);

        rig.Server.Store.IsUnavailable = true;
        rig.Server.Tick(3);
        Assert.That(rig.Server.PlayerOf(seven).IsTrading, Is.True, "held until the store answers");
        rig.Server.Store.IsUnavailable = false;
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);

        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Failed, Eight, CommandRejectionReason.ServiceUnavailable)));
        Assert.That(Bag(rig.Server, 7), Is.EqualTo($"{Gel} 10; 50"));
        Assert.That(rig.Server.SessionOf(eight).Character!.Operation, Is.Null);
    }

    [Test]
    public void ACommitWhoseAnswerIsLost_IsSettledFromTheStore_Once()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened(new TestServer(
            persistence: new PersistenceOptions
                { MaxRetries = 0, RetryBaseDelayMs = 1 }));
        rig.Server.Store.AmbiguousTradeFailures = 1;

        Agree(rig, seven, eight);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);

        Assert.That((rig.Server.Store.TradeCommits.Count, rig.Server.Store.TradeLookups.Count), Is.EqualTo((1, 1)));
        Assert.That(rig.Events(seven).Last().Kind, Is.EqualTo(TradeEventKind.Completed));
        Assert.That(Bag(rig.Server, 8), Is.EqualTo($"{Gel} 4; 50"));
        Assert.That(rig.Server.TradeLog.Entries.Select(entry => entry.EventId.Id), Does.Contain(4013));
    }

    [Test]
    public void ALogout_DuringTheCommit_WaitsForIt()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened();
        rig.Server.RunsPersistence = false;
        Agree(rig, seven, eight);
        rig.Server.Tick();

        uint logout = rig.Next(seven);
        rig.Server.SendLogout(seven, logout);
        rig.Server.Tick(2);
        Assert.That(rig.Server.SessionOf(seven).Character!.LogoutCheckpoint, Is.Null, "the logout waits");

        rig.Server.RunsPersistence = true;
        rig.Server.TickUntil(() => rig.Server.Transport.ControlSentTo(seven)
            .Any(message => message.Opcode == MessageOpcode.LogoutComplete));

        Assert.That(Bag(rig.Server, 7), Is.EqualTo($"{Gel} 6, {Sword} 1; 0"),
            "committed before the logout's checkpoint");
    }

    [Test]
    public void AnOffer_TheReceiversBagCannotHold_EndsTheTrade_NamingIt_WithNothingAskedOfTheStore()
    {
        var rig = new TradeRig();
        ConnectionId seven = rig.Enter(7, store => store.GiveItems(7, Gel, 1, 10, 0));
        ConnectionId eight = rig.Enter(8, store => store.GiveItems(8, Sword, PickupSystem.MaxInventoryRows, 1, 0));
        rig.Open(seven, eight, Seven, Eight);

        rig.Offer(seven, RowOf(rig, seven, Gel), 1);
        rig.Lock(seven);
        rig.Lock(eight);
        rig.Confirm(seven);
        rig.Confirm(eight);
        rig.Server.Tick();

        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Failed, Eight, CommandRejectionReason.InventoryFull)));
        Assert.That(rig.Events(eight).Last(),
            Is.EqualTo((TradeEventKind.Failed, Eight, CommandRejectionReason.InventoryFull)));
        Assert.That(rig.Server.Store.TradeCommits, Is.Empty);
        Assert.That(rig.Server.Trades.OpenTrades, Is.Zero);
    }

    [Test]
    public void ConfirmedOffers_CommitOnce_AndEachTraderHearsItsWholeInventoryBeforeTheCompletion()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened();

        Agree(rig, seven, eight);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);

        Assert.That(rig.Server.Store.TradeCommits, Has.Count.EqualTo(1));
        Assert.That(Bag(rig.Server, 7), Is.EqualTo($"{Gel} 6, {Sword} 1; 0"));
        Assert.That(Bag(rig.Server, 8), Is.EqualTo($"{Gel} 4; 50"));
        foreach ((ConnectionId connection, string partner) in new[] { (seven, Eight), (eight, Seven) })
        {
            MessageOpcode[] last = rig.Server.Transport.ControlSentTo(connection)
                .Select(message => message.Opcode)
                .Where(opcode => opcode == MessageOpcode.InventorySnapshot || opcode == MessageOpcode.TradeEvent)
                .ToArray();
            Assert.That(last.Skip(last.Length - 2),
                Is.EqualTo(new[] { MessageOpcode.InventorySnapshot, MessageOpcode.TradeEvent }),
                "a fresh inventory, then the completion");
            Assert.That(rig.Events(connection).Last(),
                Is.EqualTo((TradeEventKind.Completed, partner, CommandRejectionReason.None)));
            Assert.That(rig.Server.PlayerOf(connection).IsTrading, Is.False);
            Assert.That(rig.Server.SessionOf(connection).Character!.Operation, Is.Null);
        }

        Assert.That(
            rig.Server.SessionOf(seven).Character!.Inventory.Rows.Select(row => (row.Item.Value, row.Quantity)),
            Is.EquivalentTo(new[] { (Gel, 6u), (Sword, 1u) }),
            "the server's copy took the commit's answer");
        Assert.That(rig.Server.TradeLog.Entries.Select(entry => entry.EventId.Id), Does.Contain(1027));
    }

    // One trader's settle throws (its stored slot is one the server does not know); its session closes, as a fault in
    // its own boundary would close it, and the other still hears its completion.
    [Test]
    public void OneTradersFaultingSettle_ClosesItsSession_AndTheOtherSettlesOn()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened();
        rig.Server.Store.GiveItems(8, "item.material.crawler_shell", 1, 1, 0);
        rig.Server.Store.Wear(8, rig.Server.Store.Stored(8).Items.Last().Id, "Bogus");

        Agree(rig, seven, eight);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);

        Assert.That(rig.Events(seven).Last().Kind, Is.EqualTo(TradeEventKind.Completed));
        Assert.That(rig.Server.PlayerOf(seven).IsTrading, Is.False);
        Assert.That(rig.Server.Transport.Disconnects[eight], Is.EqualTo(DisconnectReason.InternalError));
    }

    [Test]
    public void WhileTheDatabaseIsKnownDown_TheCommitIsUnsaved_TheConfirmsClear_AndTheTradeStaysOpen()
    {
        (TradeRig rig, ConnectionId seven, ConnectionId eight) = Opened();
        rig.Server.Store.IsUnavailable = true;
        rig.Server.Lifetime.QueueCheckpoint(rig.Server.SessionOf(seven).Character!);
        rig.Server.Tick(2);

        Agree(rig, seven, eight);
        rig.Server.Tick();

        Assert.That(rig.Events(seven).Last(),
            Is.EqualTo((TradeEventKind.Unsaved, Eight, CommandRejectionReason.ServiceUnavailable)));
        Assert.That(rig.Server.Trades.OpenTrades, Is.EqualTo(1));
        Assert.That(rig.Sides(eight).Last().IsConfirmed, Is.False, "both confirm again");
        Assert.That(rig.Server.Store.TradeCommits, Is.Empty);
    }
}
}
