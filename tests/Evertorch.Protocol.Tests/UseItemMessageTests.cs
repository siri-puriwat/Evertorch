using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     <c>UseItem</c> of Network Protocol §8, its golden bytes, and what a reader refuses.
/// </summary>
[TestFixture]
public sealed class UseItemMessageTests
{
    private static readonly byte[] UseBytes =
    {
        0x12, 0x00,
        0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x0D, 0x0C, 0x0B, 0x0A
    };

    private static bool ReadUse(byte[] bytes)
    {
        return UseItem.TryRead(bytes, out _);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void UseItem_ForARowTheDatabaseNeverGives_IsRefused(long inventoryItem)
    {
        byte[] bytes = new byte[UseItem.EncodedLength];
        new UseItem(inventoryItem, 1).Write(bytes);

        Assert.That(UseItem.TryRead(bytes, out _), Is.False);
    }

    [Test]
    public void UseItem_ForGoldenBytes_RoundTrips()
    {
        byte[] written = new byte[UseItem.EncodedLength];
        new UseItem(0x0102030405060708L, 0x0A0B0C0D).Write(written);

        bool isRead = UseItem.TryRead(UseBytes, out UseItem read);

        Assert.That(written, Is.EqualTo(UseBytes));
        Assert.That(isRead, Is.True);
        Assert.That((read.InventoryItem, read.CommandSequence), Is.EqualTo((0x0102030405060708L, 0x0A0B0C0Du)));
    }

    [Test]
    public void UseItem_WhenMalformed_IsRefused()
    {
        WireMatrix.AssertRejectsEveryTruncation(UseBytes, ReadUse);
        WireMatrix.AssertRejectsTrailingData(UseBytes, ReadUse);
        WireMatrix.AssertRejectsOtherOpcodes(UseBytes, ReadUse);
    }
}
}
