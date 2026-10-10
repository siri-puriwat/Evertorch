using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Prices replayed on a fake clock (Gameplay Systems §11.5; Milestone 15 verification): eleven days of a seller's
///     listings and a buyer's sell-outs through the server, the board read between them, each price it shows the one the
///     history gives by hand. Gloom Dust lists at a base of 28 between 14 and 280; a period is a day, a listing sells for
///     three.
/// </summary>
[TestFixture]
public sealed class MarketPriceReplayTests
{
    [SetUp]
    public void EnterBesideTheBroker()
    {
        // The reads come faster than the read bucket allows, which other tests hold to.
        m_server = new TestServer(withNpcs: true, isAbuseControlEnabled: false);
        m_start = m_server.Time.UtcNow;
        m_seller = Enter(Seller, 100, store => store.GiveItems(Seller, Dust, 1, 50, 5));
        m_buyer = Enter(Buyer, 1000, null);
        m_server.Transport.ClearSent();
    }

    private const string Dust = "item.material.gloom_dust";
    private const long Seller = 1;
    private const long Buyer = 2;

    private TestServer m_server = null!;
    private ConnectionId m_seller;
    private ConnectionId m_buyer;
    private DateTimeOffset m_start;
    private uint m_sellerSequence;
    private uint m_buyerSequence;

    private EntityId BrokerId => m_server.NpcOf("npc.broker").Id;

    private ConnectionId Enter(long character, long coins, Action<InMemoryGameStore>? prepare)
    {
        ConnectionId player = m_server.Connect();
        m_server.SignInWithCharacter(player, character);
        prepare?.Invoke(m_server.Store);
        m_server.Store.Edit(character, coins: coins);
        m_server.SendEnterWorld(player, character);
        m_server.TickUntil(() => m_server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity broker = m_server.NpcOf("npc.broker");
        m_server.Place(player, broker.Position.X - 2f, broker.Position.Z + character - 1);
        m_server.Tick(2);
        return player;
    }

    private void At(double days)
    {
        m_server.Time.UtcNow = m_start + TimeSpan.FromDays(days);
    }

    // The answer to the command numbered sequence, once it and the reads after it have run.
    private CommandRejectionReason Settle(ConnectionId player, uint sequence)
    {
        CharacterSession character = m_server.SessionOf(player).Character!;
        m_server.Tick();
        m_server.TickUntil(() => character.Operation == null
            && !character.IsReadingMarket
            && m_server.Persistence.PendingJobs == 0);
        return m_server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read : default)
            .Where(rejected => rejected.CommandSequence == sequence)
            .Select(rejected => rejected.Reason)
            .DefaultIfEmpty(CommandRejectionReason.None)
            .Single();
    }

    // The price the board shows the buyer now, from a read of its own.
    private uint Shown()
    {
        m_server.SendMarketOpen(m_buyer, BrokerId, ++m_buyerSequence);
        Assert.That(Settle(m_buyer, m_buyerSequence), Is.EqualTo(CommandRejectionReason.None), "read");
        return m_server.Transport.ControlSentTo(m_buyer)
            .Where(message => message.Opcode == MessageOpcode.MarketBoard)
            .Select(message => MarketBoard.TryRead(message.Payload, out MarketBoard? board) ? board! : null)
            .SelectMany(board => board!.Entries)
            .Last(entry => entry.Item.Value == Dust)
            .Price;
    }

    private void ListTen()
    {
        long dust = m_server.SessionOf(m_seller).Character!.Inventory.Rows.Single(row => row.Item.Value == Dust)
            .InventoryItem;
        m_server.SendMarketList(m_seller, BrokerId, dust, 10, ++m_sellerSequence);
        Assert.That(Settle(m_seller, m_sellerSequence), Is.EqualTo(CommandRejectionReason.None), "listed");
    }

    private CommandRejectionReason Buy(uint quantity, uint limit)
    {
        m_server.SendMarketBuy(m_buyer, BrokerId, Dust, quantity, limit, ++m_buyerSequence);
        return Settle(m_buyer, m_buyerSequence);
    }

    [Test]
    public void ThePricesShown_FollowTheirHistory_DayByDay()
    {
        ListTen();
        uint listed = Shown();
        At(1);
        uint aDayLater = Shown();
        At(2.5);
        uint twoAndAHalf = Shown();
        CommandRejectionReason soldOut = Buy(10, 24);
        uint risen = Shown();
        At(5);
        uint idle = Shown();
        ListTen();
        At(5.2);
        CommandRejectionReason again = Buy(10, 26);
        uint risenAgain = Shown();
        At(5.3);
        ListTen();
        At(5.4);
        CommandRejectionReason soonAfter = Buy(10, 28);
        uint notRisen = Shown();
        At(7);
        ListTen();
        At(10);
        uint threeDaysOfStock = Shown();
        At(12);
        uint expired = Shown();
        CommandRejectionReason nothingLeft = Buy(1, 22);

        Assert.That((listed, aDayLater), Is.EqualTo((28u, 26u)), "a day of stock, a fall of a tenth");
        Assert.That(twoAndAHalf, Is.EqualTo(24u), "two whole days, two falls; the half day carried");
        Assert.That((soldOut, risen), Is.EqualTo((CommandRejectionReason.None, 26u)), "the last units, a rise");
        Assert.That(idle, Is.EqualTo(26u), "nothing listed, so the clock stood still");
        Assert.That((again, risenAgain), Is.EqualTo((CommandRejectionReason.None, 28u)), "a rise 2.7 days later");
        Assert.That((soonAfter, notRisen), Is.EqualTo((CommandRejectionReason.None, 28u)), "no second rise in a day");
        Assert.That(threeDaysOfStock, Is.EqualTo(22u), "three days of stock and the tenth carried, three falls");
        Assert.That(expired, Is.EqualTo(22u), "an expired listing holds the price no longer");
        Assert.That(nothingLeft, Is.EqualTo(CommandRejectionReason.InvalidTarget), "expired stock sells nothing");
        Assert.That(m_server.Store.Stored(Buyer).Coins, Is.EqualTo(1000L - 240 - 260 - 280), "at 24, 26, and 28");
    }
}
}
