using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedMarketMessageTests
{
    private static readonly byte[] MarketOpenBytes =
    {
        0x29, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00
    };

    private static readonly byte[] MarketListBytes =
    {
        0x2A, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0A, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00
    };

    private static readonly byte[] MarketBuyBytes =
    {
        0x2B, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x04, 0x00, 0x00, 0x00, 0x1E, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00
    };

    private static readonly byte[] MarketCollectBytes =
    {
        0x2C, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00
    };

    private static readonly byte[] MarketBoardBytes =
    {
        0x2A, 0x80, 0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61, 0x1E, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00,
        0x00
    };

    private static readonly byte[] MarketMineBytes =
    {
        0x2B, 0x80, 0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E,
        0x61, 0x06, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x6E, 0x00, 0x00, 0x00, 0x10, 0x0E, 0x00, 0x00
    };

    private static readonly byte[] MarketEventBytes =
    {
        0x2C, 0x80, 0x02, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E,
        0x61, 0x04, 0x00, 0x00, 0x00, 0x78, 0x00, 0x00, 0x00
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void ClientMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var npc = new EntityId(7);
        var buy = new MarketBuy(npc, ItemA, 4, 30, 5);

        Assert.That(Encode(MarketOpen.EncodedLength, new MarketOpen(npc, 3).Write), Is.EqualTo(MarketOpenBytes));
        Assert.That(
            Encode(MarketList.EncodedLength, new MarketList(npc, 42, 10, 4).Write),
            Is.EqualTo(MarketListBytes));
        Assert.That(Encode(buy.GetEncodedLength(), buy.Write), Is.EqualTo(MarketBuyBytes));
        Assert.That(
            Encode(MarketCollect.EncodedLength, new MarketCollect(npc, 9, 6).Write),
            Is.EqualTo(MarketCollectBytes));
        Assert.That(MarketBuy.TryRead(MarketBuyBytes, out MarketBuy? read), Is.True);
        Assert.That((read!.Item, read.Quantity, read.PriceLimit), Is.EqualTo((ItemA, 4u, 30u)));
        Assert.That(MarketCollect.TryRead(MarketCollectBytes, out MarketCollect? collect), Is.True);
        Assert.That(collect!.Listing, Is.EqualTo(9L));
    }

    [Test]
    public void ServerMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var board = new MarketBoard(new[] { new MarketBoardEntry(ItemA, 30, 6) });
        var mine = new MarketMine(new[] { new MarketListingEntry(9, ItemA, 6, 4, 110, 3600) });
        var sold = new MarketEvent(MarketEventKind.Sold, 9, ItemA, 4, 120);

        Assert.That(Encode(board.GetEncodedLength(), board.Write), Is.EqualTo(MarketBoardBytes));
        Assert.That(Encode(mine.GetEncodedLength(), mine.Write), Is.EqualTo(MarketMineBytes));
        Assert.That(Encode(sold.GetEncodedLength(), sold.Write), Is.EqualTo(MarketEventBytes));
        Assert.That(MarketBoard.TryRead(MarketBoardBytes, out MarketBoard? readBoard), Is.True);
        Assert.That(readBoard!.Entries.Single().Price, Is.EqualTo(30u));
        Assert.That(MarketMine.TryRead(MarketMineBytes, out MarketMine? readMine), Is.True);
        Assert.That(
            (readMine!.Entries.Single().CoinsWaiting, readMine.Entries.Single().SecondsLeft),
            Is.EqualTo((110u, 3600u)));
        Assert.That(MarketEvent.TryRead(MarketEventBytes, out MarketEvent? readEvent), Is.True);
        Assert.That((readEvent!.Kind, readEvent.Coins), Is.EqualTo((MarketEventKind.Sold, 120u)));
    }

    // With the longest IDs, thirteen entries fill a board message to 965 bytes and eight listings make 723, within one
    // datagram (Network Protocol §6).
    [Test]
    public void TheLargestMessages_AreNineHundredSixtyFiveAndSevenHundredTwentyThreeBytes()
    {
        var longest = new ItemDefinitionId("item." + new string('a', 59));
        MarketBoardEntry[] goods = Enumerable.Range(1, MarketBoard.MaxEntries)
            .Select(_ => new MarketBoardEntry(longest, uint.MaxValue, uint.MaxValue))
            .ToArray();
        MarketListingEntry[] listings = Enumerable.Range(1, MarketMine.MaxEntries)
            .Select(listing => new MarketListingEntry(listing, longest, 1, uint.MaxValue, uint.MaxValue, uint.MaxValue))
            .ToArray();

        Assert.That(new MarketBoard(goods).GetEncodedLength(), Is.EqualTo(965));
        Assert.That(new MarketMine(listings).GetEncodedLength(), Is.EqualTo(723));
    }
}
}
