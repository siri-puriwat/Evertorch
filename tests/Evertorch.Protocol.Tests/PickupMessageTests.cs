using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class PickupMessageTests
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

    private static bool ReadPickup(byte[] bytes)
    {
        return PickupItem.TryRead(bytes, out _);
    }

    private static bool ReadPickedUp(byte[] bytes)
    {
        return ItemPickedUp.TryRead(bytes, out _);
    }

    [TestCase(2, (byte)0x00)]
    [TestCase(20, (byte)0x6A)]
    [TestCase(26, (byte)0x00)]
    public void ItemPickedUp_WithAZeroDropABadItemOrAZeroAmount_IsRefused(int offset, byte value)
    {
        byte[] bytes = WireMatrix.With(PickedUpBytes, offset, value);

        Assert.That(ItemPickedUp.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void ItemPickedUp_ForGoldenBytes_RoundTrips()
    {
        var message = new ItemPickedUp(new EntityId(7), new EntityId(3), new ItemDefinitionId("item.a"), 2);
        byte[] written = new byte[message.GetEncodedLength()];
        message.Write(written);

        bool isRead = ItemPickedUp.TryRead(PickedUpBytes, out ItemPickedUp? read);

        Assert.That(written, Is.EqualTo(PickedUpBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Drop, Is.EqualTo(new EntityId(7)));
        Assert.That(read.Recipient, Is.EqualTo(new EntityId(3)));
        Assert.That(read.Item, Is.EqualTo(new ItemDefinitionId("item.a")));
        Assert.That(read.Amount, Is.EqualTo(2u));
    }

    [Test]
    public void ItemPickedUp_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(PickedUpBytes, ReadPickedUp);
        WireMatrix.AssertRejectsTrailingData(PickedUpBytes, ReadPickedUp);
        WireMatrix.AssertRejectsOtherOpcodes(PickedUpBytes, ReadPickedUp);
    }

    [Test]
    public void ItemPickedUp_WithAnUnknownRecipient_IsRead()
    {
        byte[] bytes = WireMatrix.With(PickedUpBytes, 10, 0x00);

        Assert.That(ItemPickedUp.TryRead(bytes, out ItemPickedUp? read), Is.True);
        Assert.That(read!.Recipient, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void PickupItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[PickupItem.EncodedLength];

        int length = new PickupItem(new EntityId(0x0123456789ABCDEF), 0x12345678).Write(written);
        bool isRead = PickupItem.TryRead(PickupBytes, out PickupItem read);

        Assert.That(length, Is.EqualTo(14));
        Assert.That(written, Is.EqualTo(PickupBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read.Drop, Is.EqualTo(new EntityId(0x0123456789ABCDEF)));
        Assert.That(read.CommandSequence, Is.EqualTo(0x12345678u));
    }

    [Test]
    public void PickupItem_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(PickupBytes, ReadPickup);
        WireMatrix.AssertRejectsTrailingData(PickupBytes, ReadPickup);
        WireMatrix.AssertRejectsOtherOpcodes(PickupBytes, ReadPickup);
    }
}
}
