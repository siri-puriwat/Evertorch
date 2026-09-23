using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Mirrors the .NET golden bytes of the pickup messages, so both compilers and runtimes agree on the wire format.
/// </summary>
[TestFixture]
public sealed class SharedPickupMessageTests
{
    private static readonly byte[] PickupBytes =
    {
        0x09, 0x00, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01, 0x78, 0x56, 0x34, 0x12
    };

    private static readonly byte[] PickedUpBytes =
    {
        0x0E, 0x80,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x02, 0x00, 0x00, 0x00
    };

    [Test]
    public void EntityDespawn_WithThePickedUpReason_RoundTrips()
    {
        byte[] buffer = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(7), DespawnReason.PickedUp).Write(buffer);

        bool isRead = EntityDespawn.TryRead(buffer, out EntityDespawn read);

        Assert.That(buffer[buffer.Length - 1], Is.EqualTo(3));
        Assert.That(isRead, Is.True);
        Assert.That(read.Reason, Is.EqualTo(DespawnReason.PickedUp));
    }

    [Test]
    public void ItemPickedUp_WriteAndRead_MatchGoldenBytes()
    {
        var message = new ItemPickedUp(new EntityId(7), new EntityId(3), new ItemDefinitionId("item.a"), 2);
        byte[] buffer = new byte[message.GetEncodedLength()];
        message.Write(buffer);

        bool isRead = ItemPickedUp.TryRead(PickedUpBytes, out ItemPickedUp? read);

        Assert.That(buffer, Is.EqualTo(PickedUpBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Recipient, Is.EqualTo(new EntityId(3)));
        Assert.That(read.Amount, Is.EqualTo(2u));
    }

    [Test]
    public void MessageRouting_ForPickupMessages_UsesTheControlChannel()
    {
        foreach (MessageOpcode opcode in new[] { MessageOpcode.PickupItem, MessageOpcode.ItemPickedUp })
        {
            Assert.That(MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery));
            Assert.That(channel, Is.EqualTo(ProtocolChannel.Control));
            Assert.That(delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        }
    }

    [Test]
    public void PickupItem_WriteAndRead_MatchGoldenBytes()
    {
        byte[] buffer = new byte[PickupItem.EncodedLength];
        new PickupItem(new EntityId(0x0123456789ABCDEF), 0x12345678).Write(buffer);

        bool isRead = PickupItem.TryRead(PickupBytes, out PickupItem read);

        Assert.That(buffer, Is.EqualTo(PickupBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }
}
}
