using System;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ShopMessageTests
{
    // Two of item.a from NPC 13, command sequence 0x0A0B0C0D.
    private static readonly byte[] BuyBytes =
    {
        0x13, 0x00, 0x0D, 0, 0, 0, 0, 0, 0, 0,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x02, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A
    };

    // Ten of row 0x0102030405060708 to NPC 13, command sequence 0x0A0B0C0D.
    private static readonly byte[] SellBytes =
    {
        0x14, 0x00, 0x0D, 0, 0, 0, 0, 0, 0, 0,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x0A, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A
    };

    private static bool ReadBuy(byte[] bytes)
    {
        return BuyItem.TryRead(bytes, out _);
    }

    private static bool ReadSell(byte[] bytes)
    {
        return SellItem.TryRead(bytes, out _);
    }

    [TestCase(2, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "NPC 0")]
    [TestCase(9, new byte[] { 0xFF }, "a negative NPC")]
    [TestCase(12, new byte[] { 0x6A }, "'jtem.a' for an item")]
    [TestCase(18, new byte[] { 0, 0, 0, 0 }, "a quantity of 0")]
    public void BuyItem_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        Assert.That(ReadBuy(WireMatrix.With(BuyBytes, offset, replacement)), Is.False, reason);
    }

    [TestCase(2, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "NPC 0")]
    [TestCase(10, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }, "row 0")]
    [TestCase(17, new byte[] { 0xFF }, "a negative row")]
    [TestCase(18, new byte[] { 0, 0, 0, 0 }, "a quantity of 0")]
    public void SellItem_WithAnImpossibleValue_IsRefused(int offset, byte[] replacement, string reason)
    {
        Assert.That(ReadSell(WireMatrix.With(SellBytes, offset, replacement)), Is.False, reason);
    }

    [Test]
    public void BuyItem_AtItsLargest_Is84Bytes_AndOfNothing_CannotBeBuilt()
    {
        var message = new BuyItem(
            new EntityId(long.MaxValue),
            new ItemDefinitionId($"item.{new string('a', 59)}"),
            uint.MaxValue,
            uint.MaxValue);
        Action none = () => _ = new BuyItem(new EntityId(13), new ItemDefinitionId("item.a"), 0, 1);

        Assert.That(message.GetEncodedLength(), Is.EqualTo(84));
        Assert.That(none, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void BuyItem_ForGoldenBytes_RoundTrips()
    {
        var message = new BuyItem(new EntityId(13), new ItemDefinitionId("item.a"), 2, 0x0A0B0C0D);
        byte[] written = new byte[message.GetEncodedLength()];
        int length = message.Write(written);

        bool isRead = BuyItem.TryRead(BuyBytes, out BuyItem? read);

        Assert.That(length, Is.EqualTo(26));
        Assert.That(written, Is.EqualTo(BuyBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read!.Npc, read.Item.Value, read.Quantity, read.CommandSequence),
            Is.EqualTo((new EntityId(13), "item.a", 2u, 0x0A0B0C0Du)));
        WireMatrix.AssertRejectsEveryTruncation(BuyBytes, ReadBuy);
        WireMatrix.AssertRejectsTrailingData(BuyBytes, ReadBuy);
        WireMatrix.AssertRejectsOtherOpcodes(BuyBytes, ReadBuy);
    }

    [Test]
    public void SellItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[SellItem.EncodedLength];
        int length = new SellItem(new EntityId(13), 0x0102030405060708L, 10, 0x0A0B0C0D).Write(written);

        bool isRead = SellItem.TryRead(SellBytes, out SellItem read);

        Assert.That(length, Is.EqualTo(26));
        Assert.That(written, Is.EqualTo(SellBytes));
        Assert.That(isRead, Is.True);
        Assert.That(
            (read.Npc, read.InventoryItem, read.Quantity, read.CommandSequence),
            Is.EqualTo((new EntityId(13), 0x0102030405060708L, 10u, 0x0A0B0C0Du)));
        WireMatrix.AssertRejectsEveryTruncation(SellBytes, ReadSell);
        WireMatrix.AssertRejectsTrailingData(SellBytes, ReadSell);
        WireMatrix.AssertRejectsOtherOpcodes(SellBytes, ReadSell);
    }
}
}
