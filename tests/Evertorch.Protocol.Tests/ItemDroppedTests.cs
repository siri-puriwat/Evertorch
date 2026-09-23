using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
[TestFixture]
public sealed class ItemDroppedTests
{
    private const int AmountOffset = 35;

    private static readonly byte[] GoldenBytes = BuildGolden();

    private static byte[] BuildGolden()
    {
        var bytes = new List<byte> { 0x0D, 0x80, 0x07, 0, 0, 0, 0, 0, 0, 0, 0x17, 0x00 };
        bytes.AddRange(Encoding.ASCII.GetBytes("item.material.slime_gel"));
        bytes.AddRange(new byte[] { 0x02, 0x00, 0x00, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x48, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x38, 0x41 });
        return bytes.ToArray();
    }

    private static bool Read(byte[] bytes)
    {
        return ItemDropped.TryRead(bytes, out _);
    }

    [Test]
    public void ItemDropped_ForGoldenBytes_RoundTrips()
    {
        var message = new ItemDropped(new EntityId(7), "item.material.slime_gel", 2,
            new WorldPosition(12.5f, 0f, 11.5f));
        byte[] written = new byte[message.GetEncodedLength()];

        int length = message.Write(written);
        bool isRead = ItemDropped.TryRead(GoldenBytes, out ItemDropped? read);

        Assert.That(length, Is.EqualTo(51));
        Assert.That(written, Is.EqualTo(GoldenBytes));
        Assert.That(isRead, Is.True);
        Assert.That(read!.Entity, Is.EqualTo(new EntityId(7)));
        Assert.That(read.ItemId, Is.EqualTo("item.material.slime_gel"));
        Assert.That(read.Amount, Is.EqualTo(2u));
        Assert.That(read.Position, Is.EqualTo(new WorldPosition(12.5f, 0f, 11.5f)));
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, Read);
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, Read);
    }

    [Test]
    public void ItemDropped_WithoutAnEntityOrAnAmountOrWithANonItemId_IsRefused()
    {
        byte[] noEntity = WireMatrix.With(GoldenBytes, 2, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] noAmount = WireMatrix.With(GoldenBytes, AmountOffset, 0x00, 0x00, 0x00, 0x00);
        byte[] notAnItem = WireMatrix.With(GoldenBytes, 12, Encoding.ASCII.GetBytes("monster"));
        byte[] notANumber = WireMatrix.With(GoldenBytes, AmountOffset + 4, 0x00, 0x00, 0xC0, 0x7F);

        Assert.That(Read(noEntity), Is.False);
        Assert.That(Read(noAmount), Is.False);
        Assert.That(Read(notAnItem), Is.False);
        Assert.That(Read(notANumber), Is.False);
    }
}
}
