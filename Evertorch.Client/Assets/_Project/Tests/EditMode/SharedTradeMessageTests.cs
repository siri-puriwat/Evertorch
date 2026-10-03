using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedTradeMessageTests
{
    private static readonly byte[] TradeRequestBytes =
    {
        0x20, 0x00, 0x05, 0x00, 0x42, 0x6F, 0x62, 0x62, 0x79, 0x07, 0x00, 0x00, 0x00
    };

    private static readonly byte[] TradeReplyBytes =
    {
        0x21, 0x00, 0x04, 0x00, 0x41, 0x6E, 0x6E, 0x61, 0x01, 0x08, 0x00, 0x00, 0x00
    };

    private static readonly byte[] TradeOfferBytes =
    {
        0x22, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00
    };

    private static readonly byte[] TradeLockBytes = { 0x23, 0x00, 0x0A, 0x00, 0x00, 0x00 };

    private static readonly byte[] TradeConfirmBytes = { 0x24, 0x00, 0x0B, 0x00, 0x00, 0x00 };

    private static readonly byte[] TradeCancelBytes = { 0x25, 0x00, 0x0C, 0x00, 0x00, 0x00 };

    private static readonly byte[] TradeEventBytes = { 0x26, 0x80, 0x07, 0x04, 0x00, 0x43, 0x6F, 0x72, 0x61, 0x05 };

    private static readonly byte[] TradeSideBytes =
    {
        0x27, 0x80, 0x02, 0x01, 0x00, 0x64, 0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x03, 0x00, 0x00, 0x00, 0x00
    };

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
        var request = new TradeRequest("Bobby", 7);
        var reply = new TradeReply("Anna", true, 8);

        Assert.That(Encode(request.GetEncodedLength(), request.Write), Is.EqualTo(TradeRequestBytes));
        Assert.That(Encode(reply.GetEncodedLength(), reply.Write), Is.EqualTo(TradeReplyBytes));
        Assert.That(Encode(TradeOffer.EncodedLength, new TradeOffer(42, 5, 9).Write), Is.EqualTo(TradeOfferBytes));
        Assert.That(Encode(TradeLock.EncodedLength, new TradeLock(10).Write), Is.EqualTo(TradeLockBytes));
        Assert.That(Encode(TradeConfirm.EncodedLength, new TradeConfirm(11).Write), Is.EqualTo(TradeConfirmBytes));
        Assert.That(Encode(TradeCancel.EncodedLength, new TradeCancel(12).Write), Is.EqualTo(TradeCancelBytes));
        Assert.That(TradeRequest.TryRead(TradeRequestBytes, out _), Is.True);
        Assert.That(TradeReply.TryRead(TradeReplyBytes, out TradeReply? read), Is.True);
        Assert.That(read!.IsAccepted, Is.True);
        Assert.That(TradeOffer.TryRead(TradeOfferBytes, out TradeOffer? offer), Is.True);
        Assert.That(offer!.Quantity, Is.EqualTo(5u));
        Assert.That(TradeLock.TryRead(TradeLockBytes, out _), Is.True);
        Assert.That(TradeConfirm.TryRead(TradeConfirmBytes, out _), Is.True);
        Assert.That(TradeCancel.TryRead(TradeCancelBytes, out _), Is.True);
    }

    [Test]
    public void ServerMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var tradeEvent = new TradeEvent(TradeEventKind.Failed, "Cora", CommandRejectionReason.InventoryFull);
        var side = new TradeSide(
            TradeSideOwner.Partner,
            true,
            false,
            100,
            new[] { new TradeEntry(0, new ItemDefinitionId("item.a"), 3) });

        Assert.That(Encode(tradeEvent.GetEncodedLength(), tradeEvent.Write), Is.EqualTo(TradeEventBytes));
        Assert.That(Encode(side.GetEncodedLength(), side.Write), Is.EqualTo(TradeSideBytes));
        Assert.That(TradeEvent.TryRead(TradeEventBytes, out TradeEvent? readEvent), Is.True);
        Assert.That(readEvent!.Reason, Is.EqualTo(CommandRejectionReason.InventoryFull));
        Assert.That(TradeSide.TryRead(TradeSideBytes, out TradeSide? readSide), Is.True);
        Assert.That((readSide!.Owner, readSide.Entries[0].Quantity), Is.EqualTo((TradeSideOwner.Partner, 3u)));
    }
}
}
