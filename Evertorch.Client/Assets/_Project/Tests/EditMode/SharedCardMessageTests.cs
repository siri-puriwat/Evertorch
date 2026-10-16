using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedCardMessageTests
{
    private static readonly byte[] InsertCardBytes =
    {
        0x2E, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x07, 0x00, 0x00, 0x00
    };

    private static readonly byte[] RemoveCardBytes =
    {
        0x2F, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x02, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B, 0x08, 0x00, 0x00, 0x00
    };

    private static readonly byte[] CardRemovalFeesBytes =
    {
        0x2F, 0x80, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x62, 0x58, 0x02, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B, 0xC8, 0x00, 0x00, 0x00
    };

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void CardMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var insert = new InsertCard(5, 42, 7);
        var remove = new RemoveCard(new EntityId(14), 42, 2, new ItemDefinitionId("item.k"), 8);
        var fees = new CardRemovalFees(
            new EntityId(14),
            0,
            1,
            new[]
            {
                new CardRemovalFee(new ItemDefinitionId("item.b"), 600),
                new CardRemovalFee(new ItemDefinitionId("item.k"), 200)
            });

        Assert.That(Encode(InsertCard.EncodedLength, insert.Write), Is.EqualTo(InsertCardBytes));
        Assert.That(Encode(remove.GetEncodedLength(), remove.Write), Is.EqualTo(RemoveCardBytes));
        Assert.That(Encode(fees.GetEncodedLength(), fees.Write), Is.EqualTo(CardRemovalFeesBytes));
        Assert.That(InsertCard.TryRead(InsertCardBytes, out InsertCard? readInsert), Is.True);
        Assert.That(readInsert!.CardItem, Is.EqualTo(5L));
        Assert.That(RemoveCard.TryRead(RemoveCardBytes, out RemoveCard? readRemove), Is.True);
        Assert.That(readRemove!.Slot, Is.EqualTo((byte)2));
        Assert.That(CardRemovalFees.TryRead(CardRemovalFeesBytes, out CardRemovalFees? readFees), Is.True);
        Assert.That(readFees!.Entries[1].Fee, Is.EqualTo(200u));
    }
}
}
