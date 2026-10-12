using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedInspectMessageTests
{
    private static readonly byte[] InspectPlayerBytes =
    {
        0x30, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00
    };

    private static readonly byte[] InspectedEquipmentBytes =
    {
        0x30, 0x80, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77, 0x07, 0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B,
        0x00, 0x00, 0x00, 0x00
    };

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void InspectMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var inspect = new InspectPlayer(new EntityId(9), 3);
        var shown = new InspectedEquipment(
            new EntityId(9),
            new InspectedItem(new ItemDefinitionId("item.w"), 7, new[] { new ItemDefinitionId("item.k") }),
            default);

        Assert.That(Encode(InspectPlayer.EncodedLength, inspect.Write), Is.EqualTo(InspectPlayerBytes));
        Assert.That(Encode(shown.GetEncodedLength(), shown.Write), Is.EqualTo(InspectedEquipmentBytes));
        Assert.That(InspectPlayer.TryRead(InspectPlayerBytes, out InspectPlayer? readInspect), Is.True);
        Assert.That(readInspect!.Entity, Is.EqualTo(new EntityId(9)));
        Assert.That(InspectedEquipment.TryRead(InspectedEquipmentBytes, out InspectedEquipment? readShown), Is.True);
        Assert.That(readShown!.Weapon.RefineLevel, Is.EqualTo((byte)7));
        Assert.That(readShown.Armor.IsEmpty, Is.True);
    }
}
}
