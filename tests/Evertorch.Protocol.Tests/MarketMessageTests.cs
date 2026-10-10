using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 39, the market board (Network Protocol §6): four commands at the Broker and three messages to the
///     player, pinned by their golden bytes, their largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class MarketMessageTests
{
    private static readonly byte[] OpenBytes =
    {
        0x29, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00
    };

    private static readonly byte[] ListBytes =
    {
        0x2A, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0A, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00
    };

    // Four of item.a at 30 coins a unit at most.
    private static readonly byte[] BuyBytes =
    {
        0x2B, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x04, 0x00, 0x00, 0x00,
        0x1E, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00
    };

    private static readonly byte[] CollectBytes =
    {
        0x2C, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00
    };

    // item.a at 30 coins, six units for sale.
    private static readonly byte[] BoardBytes =
    {
        0x2A, 0x80,
        0x01,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x1E, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00
    };

    // Listing 9 of item.a: six left, four sold, 110 coins waiting, an hour to go.
    private static readonly byte[] MineBytes =
    {
        0x2B, 0x80,
        0x01,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x06, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00,
        0x6E, 0x00, 0x00, 0x00,
        0x10, 0x0E, 0x00, 0x00
    };

    // Four units of listing 9's item.a sold for 120 coins.
    private static readonly byte[] EventBytes =
    {
        0x2C, 0x80,
        0x02,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x04, 0x00, 0x00, 0x00,
        0x78, 0x00, 0x00, 0x00
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");
    private static readonly ItemDefinitionId LongestItem = new("item." + new string('a', 59));

    public static MarketOpen OpenGolden => new(new EntityId(7), 3);

    public static MarketList ListGolden => new(new EntityId(7), 42, 10, 4);

    public static MarketBuy BuyGolden => new(new EntityId(7), ItemA, 4, 30, 5);

    public static MarketCollect CollectGolden => new(new EntityId(7), 9, 6);

    public static MarketBoard BoardGolden => new(new[] { new MarketBoardEntry(ItemA, 30, 6) });

    public static MarketMine MineGolden => new(new[] { new MarketListingEntry(9, ItemA, 6, 4, 110, 3600) });

    public static MarketEvent EventGolden => new(MarketEventKind.Sold, 9, ItemA, 4, 120);

    public static MarketBoard BoardLargest => new(
        Enumerable.Range(1, MarketBoard.MaxEntries)
            .Select(_ => new MarketBoardEntry(LongestItem, uint.MaxValue, uint.MaxValue))
            .ToArray());

    public static MarketMine MineLargest => new(
        Enumerable.Range(1, MarketMine.MaxEntries)
            .Select(listing =>
                new MarketListingEntry(listing, LongestItem, 1, uint.MaxValue, uint.MaxValue, uint.MaxValue))
            .ToArray());

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Changed(byte[] golden, int at, params byte[] values)
    {
        byte[] bytes = (byte[])golden.Clone();
        values.CopyTo(bytes, at);
        return bytes;
    }

    [Test]
    public void Board_OutOfItsRules_IsMalformed()
    {
        Assert.That(MarketBoard.TryRead(Changed(BoardBytes, 2, 0x00), out _), Is.False, "no entries");
        Assert.That(MarketBoard.TryRead(Changed(BoardBytes, 2, 0x0E), out _), Is.False, "fourteen entries");
        Assert.That(MarketBoard.TryRead(Changed(BoardBytes, 11, 0x00), out _), Is.False, "a price of 0");
        Assert.That(MarketBoard.TryRead(Changed(BoardBytes, 5, 0x58), out _), Is.False, "an item of no kind");
        Assert.That(MarketBoard.TryRead(BoardBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    [Test]
    public void ClientMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(MarketOpen.EncodedLength, bytes => OpenGolden.Write(bytes)), Is.EqualTo(OpenBytes));
        Assert.That(Encode(MarketList.EncodedLength, bytes => ListGolden.Write(bytes)), Is.EqualTo(ListBytes));
        Assert.That(Encode(BuyGolden.GetEncodedLength(), bytes => BuyGolden.Write(bytes)), Is.EqualTo(BuyBytes));
        Assert.That(
            Encode(MarketCollect.EncodedLength, bytes => CollectGolden.Write(bytes)),
            Is.EqualTo(CollectBytes));

        Assert.That(MarketOpen.TryRead(OpenBytes, out MarketOpen? open), Is.True);
        Assert.That((open!.Npc, open.CommandSequence), Is.EqualTo((new EntityId(7), 3u)));
        Assert.That(MarketList.TryRead(ListBytes, out MarketList? list), Is.True);
        Assert.That(
            (list!.Npc, list.InventoryItem, list.Quantity, list.CommandSequence),
            Is.EqualTo((new EntityId(7), 42L, 10u, 4u)));
        Assert.That(MarketBuy.TryRead(BuyBytes, out MarketBuy? buy), Is.True);
        Assert.That(
            (buy!.Npc, buy.Item, buy.Quantity, buy.PriceLimit, buy.CommandSequence),
            Is.EqualTo((new EntityId(7), ItemA, 4u, 30u, 5u)));
        Assert.That(MarketCollect.TryRead(CollectBytes, out MarketCollect? collect), Is.True);
        Assert.That(
            (collect!.Npc, collect.Listing, collect.CommandSequence),
            Is.EqualTo((new EntityId(7), 9L, 6u)));
    }

    [Test]
    public void Commands_WithoutARowAListingAQuantityOrALimit_AreMalformed()
    {
        Assert.That(MarketList.TryRead(Changed(ListBytes, 10, new byte[8]), out _), Is.False, "no row");
        Assert.That(MarketList.TryRead(Changed(ListBytes, 18, new byte[4]), out _), Is.False, "no quantity");
        Assert.That(MarketBuy.TryRead(Changed(BuyBytes, 18, new byte[4]), out _), Is.False, "no quantity");
        Assert.That(MarketBuy.TryRead(Changed(BuyBytes, 22, new byte[4]), out _), Is.False, "a limit of 0");
        Assert.That(MarketBuy.TryRead(Changed(BuyBytes, 12, 0x6E), out _), Is.False, "an item of no kind");
        Assert.That(MarketCollect.TryRead(Changed(CollectBytes, 10, new byte[8]), out _), Is.False, "no listing");
        Assert.That(MarketOpen.TryRead(OpenBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        Action rowless = () => _ = new MarketList(new EntityId(7), 0, 1, 1);
        Action nothing = () => _ = new MarketBuy(new EntityId(7), ItemA, 0, 1, 1);
        Action free = () => _ = new MarketBuy(new EntityId(7), ItemA, 1, 0, 1);
        Action listingless = () => _ = new MarketCollect(new EntityId(7), 0, 1);
        Action empty = () => _ = new MarketBoard(Array.Empty<MarketBoardEntry>());
        Action priceless = () => _ = new MarketBoard(new[] { new MarketBoardEntry(ItemA, 0, 1) });
        MarketListingEntry[] nine = Enumerable.Range(1, 9)
            .Select(listing => new MarketListingEntry(listing, ItemA, 1, 0, 0, 0))
            .ToArray();
        Action tooMany = () => _ = new MarketMine(nine);
        Action unpaid = () => _ = new MarketEvent(MarketEventKind.Listed, 9, ItemA, 1, 0);
        Action nowhere = () => _ = new MarketEvent(MarketEventKind.Sold, 0, ItemA, 1, 1);

        Assert.That(rowless, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(nothing, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(free, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(listingless, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(empty, Throws.ArgumentException);
        Assert.That(priceless, Throws.ArgumentException);
        Assert.That(tooMany, Throws.ArgumentException);
        Assert.That(unpaid, Throws.ArgumentException);
        Assert.That(nowhere, Throws.ArgumentException);
    }

    [Test]
    public void CreateParts_SplitsTheBoardIntoUpsertsOfThirteen_AndAnEmptyBoardIntoNone()
    {
        MarketBoardEntry[] goods = Enumerable.Range(1, 22)
            .Select(index => new MarketBoardEntry(new ItemDefinitionId($"item.good_{index:D2}"), (uint)index, 0))
            .ToArray();

        IReadOnlyList<MarketBoard> parts = MarketBoard.CreateParts(goods);

        Assert.That(parts.Select(part => part.Entries.Count), Is.EqualTo(new[] { 13, 9 }));
        Assert.That(parts.SelectMany(part => part.Entries).Select(entry => entry.Price), Is.EqualTo(
            Enumerable.Range(1, 22).Select(index => (uint)index)));
        Assert.That(MarketBoard.CreateParts(goods.Take(7).ToArray()).Single().Entries, Has.Count.EqualTo(7));
        Assert.That(MarketBoard.CreateParts(Array.Empty<MarketBoardEntry>()), Is.Empty);
    }

    [Test]
    public void Event_OutOfItsRules_IsMalformed_AndACollectionCarriesUnitsOrCoins()
    {
        var unitsOnly = new MarketEvent(MarketEventKind.Collected, 9, ItemA, 6, 0);
        var coinsOnly = new MarketEvent(MarketEventKind.Collected, 9, ItemA, 0, 110);
        byte[] neither = Encode(unitsOnly.GetEncodedLength(), bytes => unitsOnly.Write(bytes));
        new byte[4].CopyTo(neither, 19);

        Assert.That(
            MarketEvent.TryRead(Encode(unitsOnly.GetEncodedLength(), bytes => unitsOnly.Write(bytes)), out _),
            Is.True);
        Assert.That(
            MarketEvent.TryRead(Encode(coinsOnly.GetEncodedLength(), bytes => coinsOnly.Write(bytes)), out _),
            Is.True);
        Assert.That(MarketEvent.TryRead(neither, out _), Is.False, "a collection of nothing");
        Assert.That(MarketEvent.TryRead(Changed(EventBytes, 2, 0x00), out _), Is.False, "no kind");
        Assert.That(MarketEvent.TryRead(Changed(EventBytes, 2, 0x04), out _), Is.False, "a kind past collected");
        Assert.That(MarketEvent.TryRead(Changed(EventBytes, 3, new byte[8]), out _), Is.False, "no listing");
        Assert.That(MarketEvent.TryRead(Changed(EventBytes, 23, new byte[4]), out _), Is.False, "a sale for 0");
        Assert.That(MarketEvent.TryRead(EventBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    [Test]
    public void Mine_OutOfItsRules_IsMalformed()
    {
        Assert.That(MarketMine.TryRead(Changed(MineBytes, 2, 0x09), out _), Is.False, "nine listings");
        Assert.That(MarketMine.TryRead(Changed(MineBytes, 3, new byte[8]), out _), Is.False, "a listing of 0");
        Assert.That(
            MarketMine.TryRead(Changed(Changed(MineBytes, 19, new byte[4]), 23, new byte[4]), out _),
            Is.False,
            "a listing of nothing");
        Assert.That(MarketMine.TryRead(MineBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
        Assert.That(MarketMine.TryRead(new byte[] { 0x2B, 0x80, 0x00 }, out MarketMine? none), Is.True, "none yet");
        Assert.That(none!.Entries, Is.Empty);
    }

    [Test]
    public void ServerMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(BoardGolden.GetEncodedLength(), bytes => BoardGolden.Write(bytes)), Is.EqualTo(BoardBytes));
        Assert.That(Encode(MineGolden.GetEncodedLength(), bytes => MineGolden.Write(bytes)), Is.EqualTo(MineBytes));
        Assert.That(Encode(EventGolden.GetEncodedLength(), bytes => EventGolden.Write(bytes)), Is.EqualTo(EventBytes));

        Assert.That(MarketBoard.TryRead(BoardBytes, out MarketBoard? board), Is.True);
        MarketBoardEntry entry = board!.Entries.Single();
        Assert.That((entry.Item, entry.Price, entry.Stock), Is.EqualTo((ItemA, 30u, 6u)));
        Assert.That(MarketMine.TryRead(MineBytes, out MarketMine? mine), Is.True);
        MarketListingEntry listing = mine!.Entries.Single();
        Assert.That(
            (listing.Listing, listing.Item, listing.UnitsLeft, listing.UnitsSold, listing.CoinsWaiting,
                listing.SecondsLeft),
            Is.EqualTo((9L, ItemA, 6u, 4u, 110u, 3600u)));
        Assert.That(MarketEvent.TryRead(EventBytes, out MarketEvent? sold), Is.True);
        Assert.That(
            (sold!.Kind, sold.Listing, sold.Item, sold.Quantity, sold.Coins),
            Is.EqualTo((MarketEventKind.Sold, 9L, ItemA, 4u, 120u)));
    }

    // With the longest IDs, thirteen entries fill a board message to 965 bytes, eight listings make 723, a notice 85,
    // and a purchase 88, each within one datagram's 1,020 (Network Protocol §6).
    [Test]
    public void TheLargestMessages_FitOneDatagram()
    {
        MarketBoard board = BoardLargest;
        MarketMine mine = MineLargest;
        var notice = new MarketEvent(MarketEventKind.Collected, long.MaxValue, LongestItem, uint.MaxValue,
            uint.MaxValue);
        var buy = new MarketBuy(new EntityId(long.MaxValue), LongestItem, uint.MaxValue, uint.MaxValue, uint.MaxValue);

        Assert.That(
            (board.GetEncodedLength(), mine.GetEncodedLength(), notice.GetEncodedLength(), buy.GetEncodedLength()),
            Is.EqualTo((965, 723, 85, 88)));
        Assert.That((MarketEvent.MaxEncodedLength, MarketBuy.MaxEncodedLength), Is.EqualTo((85, 88)));
        Assert.That(MarketBoard.TryRead(Encode(board.GetEncodedLength(), bytes => board.Write(bytes)), out _), Is.True);
        Assert.That(MarketMine.TryRead(Encode(mine.GetEncodedLength(), bytes => mine.Write(bytes)), out _), Is.True);
    }
}
}
