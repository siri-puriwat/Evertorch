using System;
using System.Linq;
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

    // Protocol 49: player 9, answering command 0x0A0B0C0D at revision 3, part 1 of 1, item.w at +7 holding item.k as
    // the weapon and the seven other positions empty.
    private static readonly byte[] InspectedEquipmentBytes =
    {
        0x30, 0x80, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0C, 0x0B, 0x0A, 0x03, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x08,
        0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77, 0x07, 0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E,
        0x6B,
        0x02, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00, 0x00
    };

    // Player 9 at revision 3 wears item.w as its weapon and item.c as accessory 2, and nothing else.
    private static readonly byte[] EquipmentAppearanceBytes =
    {
        0x31, 0x80, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x63
    };

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void EquipmentAppearance_WritesItsGoldenBytes_AndReadsThemBack()
    {
        var sword = new ItemDefinitionId("item.w");
        var charm = new ItemDefinitionId("item.c");
        var appearance = new EquipmentAppearance(
            new EntityId(9),
            3,
            new[] { sword, default, default, default, default, default, default, charm });

        Assert.That(Encode(appearance.GetEncodedLength(), appearance.Write), Is.EqualTo(EquipmentAppearanceBytes));
        Assert.That(EquipmentAppearance.TryRead(EquipmentAppearanceBytes, out EquipmentAppearance? read), Is.True);
        Assert.That(read!.Revision, Is.EqualTo(3u));
        Assert.That(read.ItemIn(EquipmentSlot.Weapon), Is.EqualTo(sword));
        Assert.That(read.ItemIn(EquipmentSlot.Accessory2), Is.EqualTo(charm));
        Assert.That(read.ItemIn(EquipmentSlot.Shield), Is.EqualTo(default(ItemDefinitionId)));
    }

    [Test]
    public void InspectMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var inspect = new InspectPlayer(new EntityId(9), 3);
        InspectedEntry[] entries = EquipmentPositions.All
            .Select(slot => new InspectedEntry(
                slot,
                slot == EquipmentSlot.Weapon
                    ? new InspectedItem(new ItemDefinitionId("item.w"), 7, new[] { new ItemDefinitionId("item.k") })
                    : default))
            .ToArray();
        var shown = new InspectedEquipment(new EntityId(9), 0x0A0B0C0D, 3, 0, 1, entries);

        Assert.That(Encode(InspectPlayer.EncodedLength, inspect.Write), Is.EqualTo(InspectPlayerBytes));
        Assert.That(Encode(shown.GetEncodedLength(), shown.Write), Is.EqualTo(InspectedEquipmentBytes));
        Assert.That(InspectPlayer.TryRead(InspectPlayerBytes, out InspectPlayer? readInspect), Is.True);
        Assert.That(readInspect!.Entity, Is.EqualTo(new EntityId(9)));
        Assert.That(InspectedEquipment.TryRead(InspectedEquipmentBytes, out InspectedEquipment? readShown), Is.True);
        Assert.That(readShown!.CommandSequence, Is.EqualTo(0x0A0B0C0Du));
        Assert.That(readShown.Entries[0].Item.RefineLevel, Is.EqualTo((byte)7));
        Assert.That(readShown.Entries.Skip(1).All(entry => entry.Item.IsEmpty), Is.True);
    }
}
}
