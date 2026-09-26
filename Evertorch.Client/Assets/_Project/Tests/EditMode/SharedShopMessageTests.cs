using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the shop's commands, so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedShopMessageTests
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

    [Test]
    public void BuyItem_WriteAndRead_MatchGoldenBytes()
    {
        var message = new BuyItem(new EntityId(13), new ItemDefinitionId("item.a"), 2, 0x0A0B0C0D);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = BuyItem.TryRead(BuyBytes, out BuyItem? read);

        Assert.That(buffer, Is.EqualTo(BuyBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Npc, Is.EqualTo(new EntityId(13)));
        Assert.That(read.Item.Value, Is.EqualTo("item.a"));
        Assert.That(read.Quantity, Is.EqualTo(2u));
        Assert.That(read.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
    }

    [Test]
    public void SellItem_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[SellItem.EncodedLength];
        new SellItem(new EntityId(13), 0x0102030405060708L, 10, 0x0A0B0C0D).Write(buffer);

        bool isRead = SellItem.TryRead(SellBytes, out SellItem read);

        Assert.That(buffer, Is.EqualTo(SellBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.InventoryItem, Is.EqualTo(0x0102030405060708L));
        Assert.That(read.Quantity, Is.EqualTo(10u));
    }

    [Test]
    public void ShopCommands_RouteOnTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[] { MessageOpcode.BuyItem, MessageOpcode.SellItem })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }
}
}
