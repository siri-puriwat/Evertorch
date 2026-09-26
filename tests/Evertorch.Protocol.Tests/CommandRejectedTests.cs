using System;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class CommandRejectedTests
{
    private static readonly byte[] GoldenBytes = { 0x18, 0x80, 0x78, 0x56, 0x34, 0x12, 0x07 };

    private static bool Read(byte[] bytes)
    {
        return CommandRejected.TryRead(bytes, out _);
    }

    [TestCase((byte)0)]
    [TestCase((byte)12)]
    [TestCase((byte)255)]
    public void CommandRejected_WithAnUnknownReason_IsRefused(byte reason)
    {
        Assert.That(Read(WireMatrix.With(GoldenBytes, 6, reason)), Is.False);
    }

    [TestCase(CommandRejectionReason.NotEnoughCoins)]
    [TestCase(CommandRejectionReason.CoinCapReached)]
    public void CommandRejected_WithAShopReason_IsRead(CommandRejectionReason reason)
    {
        bool isRead = CommandRejected.TryRead(WireMatrix.With(GoldenBytes, 6, (byte)reason), out CommandRejected read);

        Assert.That((isRead, read.Reason), Is.EqualTo((true, reason)));
    }

    [Test]
    public void CommandRejected_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[CommandRejected.EncodedLength];

        int length = new CommandRejected(0x12345678, CommandRejectionReason.Busy).Write(written);
        bool isRead = CommandRejected.TryRead(GoldenBytes, out CommandRejected read);

        Assert.That(length, Is.EqualTo(7));
        Assert.That(written, Is.EqualTo(GoldenBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
        Assert.That(read.Reason, Is.EqualTo(CommandRejectionReason.Busy));
    }

    [Test]
    public void CommandRejected_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, Read);
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, Read);
    }

    [Test]
    public void CommandRejectionReason_Values_KeepTheirWireNumbers()
    {
        Assert.That(
            Array.ConvertAll(
                (CommandRejectionReason[])Enum.GetValues(typeof(CommandRejectionReason)),
                reason => (byte)reason),
            Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }));
        Assert.That(CommandRejectionReason.InvalidTarget, Is.EqualTo((CommandRejectionReason)1));
        Assert.That(CommandRejectionReason.OutOfRange, Is.EqualTo((CommandRejectionReason)2));
        Assert.That(CommandRejectionReason.NotAllowedNow, Is.EqualTo((CommandRejectionReason)3));
        Assert.That(CommandRejectionReason.LootPriority, Is.EqualTo((CommandRejectionReason)4));
        Assert.That(CommandRejectionReason.InventoryFull, Is.EqualTo((CommandRejectionReason)5));
        Assert.That(CommandRejectionReason.ServiceUnavailable, Is.EqualTo((CommandRejectionReason)6));
        Assert.That(CommandRejectionReason.Busy, Is.EqualTo((CommandRejectionReason)7));
        Assert.That(CommandRejectionReason.NotEnoughSp, Is.EqualTo((CommandRejectionReason)8));
        Assert.That(CommandRejectionReason.ItemActionInFlight, Is.EqualTo((CommandRejectionReason)9));
        Assert.That(CommandRejectionReason.NotEnoughCoins, Is.EqualTo((CommandRejectionReason)10));
        Assert.That(CommandRejectionReason.CoinCapReached, Is.EqualTo((CommandRejectionReason)11));
    }
}
}
