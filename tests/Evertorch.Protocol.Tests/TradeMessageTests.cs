using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 37, the face-to-face trade (Network Protocol §6): six commands and two messages to the traders, pinned
///     by their golden bytes, their largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class TradeMessageTests
{
    private static readonly byte[] RequestBytes =
    {
        0x20, 0x00,
        0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79,
        0x07, 0x00, 0x00, 0x00
    };

    private static readonly byte[] ReplyBytes =
    {
        0x21, 0x00,
        0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61,
        0x01,
        0x08, 0x00, 0x00, 0x00
    };

    private static readonly byte[] OfferBytes =
    {
        0x22, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x00, 0x00
    };

    private static readonly byte[] LockBytes = { 0x23, 0x00, 0x0A, 0x00, 0x00, 0x00 };

    private static readonly byte[] ConfirmBytes = { 0x24, 0x00, 0x0B, 0x00, 0x00, 0x00 };

    private static readonly byte[] CancelBytes = { 0x25, 0x00, 0x0C, 0x00, 0x00, 0x00 };

    // The commit refused the trade: Cora's bag is full (reason 5).
    private static readonly byte[] EventBytes = { 0x26, 0x80, 0x07, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61, 0x05 };

    // The partner's side, locked, not confirmed, 100 coins and three of item.a, its row's ID left out.
    private static readonly byte[] SideBytes =
    {
        0x27, 0x80,
        0x02,
        0x01,
        0x00,
        0x64, 0x00, 0x00, 0x00,
        0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x03, 0x00, 0x00, 0x00,
        0x00
    };

    public static TradeRequest RequestGolden => new("Bobby", 7);

    public static TradeReply ReplyGolden => new("Anna", true, 8);

    public static TradeOffer OfferGolden => new(42, 5, 9);

    public static TradeLock LockGolden => new(10);

    public static TradeConfirm ConfirmGolden => new(11);

    public static TradeCancel CancelGolden => new(12);

    public static TradeEvent EventGolden => new(TradeEventKind.Failed, "Cora", CommandRejectionReason.InventoryFull);

    public static TradeSide SideGolden => new(
        TradeSideOwner.Partner,
        true,
        false,
        100,
        new[] { new TradeEntry(0, new ItemDefinitionId("item.a"), 3) });

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Encode(TradeSide side)
    {
        return Encode(side.GetEncodedLength(), bytes => side.Write(bytes));
    }

    private static byte[] Encode(TradeEvent tradeEvent)
    {
        return Encode(tradeEvent.GetEncodedLength(), bytes => tradeEvent.Write(bytes));
    }

    private static TradeSide Side(TradeSideOwner owner, params TradeEntry[] entries)
    {
        return new TradeSide(owner, false, false, 0, entries);
    }

    [TestCase((byte)0)]
    [TestCase((byte)9)]
    [TestCase((byte)255)]
    public void Event_OfAnUnknownKind_IsMalformed(byte kind)
    {
        byte[] bytes = (byte[])EventBytes.Clone();
        bytes[2] = kind;

        Assert.That(TradeEvent.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void ClientMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(RequestGolden.GetEncodedLength(), bytes => RequestGolden.Write(bytes)),
            Is.EqualTo(RequestBytes));
        Assert.That(Encode(ReplyGolden.GetEncodedLength(), bytes => ReplyGolden.Write(bytes)), Is.EqualTo(ReplyBytes));
        Assert.That(Encode(TradeOffer.EncodedLength, bytes => OfferGolden.Write(bytes)), Is.EqualTo(OfferBytes));
        Assert.That(Encode(TradeLock.EncodedLength, bytes => LockGolden.Write(bytes)), Is.EqualTo(LockBytes));
        Assert.That(Encode(TradeConfirm.EncodedLength, bytes => ConfirmGolden.Write(bytes)), Is.EqualTo(ConfirmBytes));
        Assert.That(Encode(TradeCancel.EncodedLength, bytes => CancelGolden.Write(bytes)), Is.EqualTo(CancelBytes));
        Assert.That(TradeRequest.TryRead(RequestBytes, out TradeRequest? request), Is.True);
        Assert.That((request!.Partner, request.CommandSequence), Is.EqualTo(("Bobby", 7u)));
        Assert.That(TradeReply.TryRead(ReplyBytes, out TradeReply? reply), Is.True);
        Assert.That((reply!.Requester, reply.IsAccepted, reply.CommandSequence), Is.EqualTo(("Anna", true, 8u)));
        Assert.That(TradeOffer.TryRead(OfferBytes, out TradeOffer? offer), Is.True);
        Assert.That((offer!.InventoryItem, offer.Quantity, offer.CommandSequence, offer.IsCoins),
            Is.EqualTo((42L, 5u, 9u, false)));
        Assert.That(TradeLock.TryRead(LockBytes, out TradeLock? locked), Is.True);
        Assert.That(locked!.CommandSequence, Is.EqualTo(10u));
        Assert.That(TradeConfirm.TryRead(ConfirmBytes, out TradeConfirm? confirm), Is.True);
        Assert.That(confirm!.CommandSequence, Is.EqualTo(11u));
        Assert.That(TradeCancel.TryRead(CancelBytes, out TradeCancel? cancel), Is.True);
        Assert.That(cancel!.CommandSequence, Is.EqualTo(12u));
    }

    [Test]
    public void Commands_ThatBreakARule_AreMalformed()
    {
        byte[] reply = (byte[])ReplyBytes.Clone();
        reply[8] = 2;
        byte[] negative = (byte[])OfferBytes.Clone();
        BitConverter.GetBytes(-1L).CopyTo(negative, 2);
        byte[] pastTheCap = (byte[])OfferBytes.Clone();
        BitConverter.GetBytes(ContentLimits.MaxCurrency + 1).CopyTo(pastTheCap, 10);
        var badName = new TradeRequest("Ab cd", 1);

        Assert.That(TradeReply.TryRead(reply, out _), Is.False, "an answer beyond 1");
        Assert.That(TradeOffer.TryRead(negative, out _), Is.False, "a negative row");
        Assert.That(TradeOffer.TryRead(pastTheCap, out _), Is.False, "a quantity past the coin cap");
        Assert.That(
            TradeRequest.TryRead(Encode(badName.GetEncodedLength(), bytes => badName.Write(bytes)), out _),
            Is.False,
            "the name rule");
    }

    [Test]
    public void Event_WithAReasonItsKindDoesNotTake_OrWithoutOne_IsMalformed()
    {
        byte[] failedWithout = (byte[])EventBytes.Clone();
        failedWithout[^1] = 0;
        byte[] openedWith = (byte[])EventBytes.Clone();
        openedWith[2] = (byte)TradeEventKind.Opened;
        byte[] unknownReason = (byte[])EventBytes.Clone();
        unknownReason[^1] = 200;

        Assert.That(TradeEvent.TryRead(failedWithout, out _), Is.False, "a failure says why");
        Assert.That(TradeEvent.TryRead(openedWith, out _), Is.False, "an opening says nothing more");
        Assert.That(TradeEvent.TryRead(unknownReason, out _), Is.False, "an unknown reason");
        Assert.That(TradeEvent.TryRead(Encode(new TradeEvent(TradeEventKind.Opened, "Ann1")), out _), Is.True);
        Assert.That(TradeEvent.TryRead(Encode(new TradeEvent(TradeEventKind.Opened, "Ann")), out _), Is.False,
            "the name rule");
    }

    [Test]
    public void EveryMessage_CutShortOrTrailedOrMisnamed_IsMalformed()
    {
        WireMatrix.AssertRejectsEveryTruncation(RequestBytes, bytes => TradeRequest.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(ReplyBytes, bytes => TradeReply.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(OfferBytes, bytes => TradeOffer.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(LockBytes, bytes => TradeLock.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(ConfirmBytes, bytes => TradeConfirm.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(CancelBytes, bytes => TradeCancel.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(EventBytes, bytes => TradeEvent.TryRead(bytes, out _));
        WireMatrix.AssertRejectsEveryTruncation(SideBytes, bytes => TradeSide.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(RequestBytes, bytes => TradeRequest.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(ReplyBytes, bytes => TradeReply.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(OfferBytes, bytes => TradeOffer.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(LockBytes, bytes => TradeLock.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(ConfirmBytes, bytes => TradeConfirm.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(CancelBytes, bytes => TradeCancel.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(EventBytes, bytes => TradeEvent.TryRead(bytes, out _));
        WireMatrix.AssertRejectsTrailingData(SideBytes, bytes => TradeSide.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(OfferBytes, bytes => TradeOffer.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(EventBytes, bytes => TradeEvent.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(SideBytes, bytes => TradeSide.TryRead(bytes, out _));
    }

    [Test]
    public void ServerMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(EventGolden), Is.EqualTo(EventBytes));
        Assert.That(Encode(SideGolden), Is.EqualTo(SideBytes));
        Assert.That(TradeEvent.TryRead(EventBytes, out TradeEvent? tradeEvent), Is.True);
        Assert.That(
            (tradeEvent!.Kind, tradeEvent.Name, tradeEvent.Reason),
            Is.EqualTo((TradeEventKind.Failed, "Cora", CommandRejectionReason.InventoryFull)));
        Assert.That(TradeSide.TryRead(SideBytes, out TradeSide? side), Is.True);
        Assert.That(
            (side!.Owner, side.IsLocked, side.IsConfirmed, side.Coins),
            Is.EqualTo((TradeSideOwner.Partner, true, false, 100u)));
        Assert.That(
            side.Entries.Select(entry => (entry.InventoryItem, entry.Item.Value, entry.Quantity, entry.RefineLevel)),
            Is.EqualTo(new[] { (0L, "item.a", 3u, (byte)0) }));
    }

    // Ten rows at every limit fit one message and the WebSocket's 1,020 bytes (Network Protocol §7).
    [Test]
    public void Side_OfTenAtEveryLimit_IsEightHundredBytes_AndTheLargestEventTwentyNine()
    {
        TradeEntry[] entries = Enumerable.Range(0, TradeSide.MaxEntries)
            .Select(index => new TradeEntry(
                long.MaxValue - index,
                new ItemDefinitionId($"item.{new string('i', DefinitionIdLimits.MaxLength - 5)}"),
                1,
                byte.MaxValue))
            .ToArray();
        string longest = new('A', CharacterNames.MaxLength);

        byte[] bytes = Encode(new TradeSide(TradeSideOwner.Own, true, true, ContentLimits.MaxCurrency, entries));

        Assert.That(bytes, Has.Length.EqualTo(800));
        Assert.That(TradeSide.TryRead(bytes, out TradeSide? read), Is.True);
        Assert.That(read!.Entries, Has.Count.EqualTo(10));
        Assert.That(Encode(new TradeEvent(TradeEventKind.Unsaved, longest, CommandRejectionReason.ServiceUnavailable)),
            Has.Length.EqualTo(29));
        Assert.That(new TradeReply(longest, true, uint.MaxValue).GetEncodedLength(), Is.EqualTo(32));
        Assert.That(new TradeRequest(longest, uint.MaxValue).GetEncodedLength(), Is.EqualTo(31));
    }

    [Test]
    public void Side_ThatBreaksARule_IsMalformed()
    {
        var item = new ItemDefinitionId("item.a");
        byte[] elevenRows = Encode(Side(TradeSideOwner.Own, new TradeEntry(1, item, 1)));
        elevenRows[9] = 11;
        byte[] unknownOwner = (byte[])SideBytes.Clone();
        unknownOwner[2] = 3;
        byte[] lockedTwice = (byte[])SideBytes.Clone();
        lockedTwice[3] = 2;
        byte[] pastTheCap = Encode(new TradeSide(TradeSideOwner.Own, false, false, 0, Array.Empty<TradeEntry>()));
        BitConverter.GetBytes(ContentLimits.MaxCurrency + 1).CopyTo(pastTheCap, 5);

        Assert.That(TradeSide.TryRead(elevenRows, out _), Is.False, "more than ten rows");
        Assert.That(TradeSide.TryRead(unknownOwner, out _), Is.False, "an unknown owner");
        Assert.That(TradeSide.TryRead(lockedTwice, out _), Is.False, "a flag beyond 1");
        Assert.That(TradeSide.TryRead(pastTheCap, out _), Is.False, "coins past the cap");
        Assert.That(TradeSide.TryRead(Encode(Side(TradeSideOwner.Partner, new TradeEntry(5, item, 1))), out _),
            Is.False, "a row's ID on the partner's side");
        Assert.That(TradeSide.TryRead(Encode(Side(TradeSideOwner.Own, new TradeEntry(0, item, 1))), out _),
            Is.False, "no ID on one's own side");
        Assert.That(TradeSide.TryRead(Encode(Side(TradeSideOwner.Own, new TradeEntry(5, item, 0))), out _),
            Is.False, "nothing of a row");
        Assert.That(TradeSide.TryRead(Encode(Side(TradeSideOwner.Own, new TradeEntry(5, item, 2, 1))), out _),
            Is.False, "a refine level on a stack");
        Assert.That(TradeSide.TryRead(Encode(Side(TradeSideOwner.Own, new TradeEntry(5, item, 1, 7))), out _),
            Is.True, "a refined row of one");
    }
}
}
