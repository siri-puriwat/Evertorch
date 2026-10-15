using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 49's appearance (Network Protocol §6): the item in each of the eight positions and the revision, pinned
///     by golden bytes, its sizes, and every rule its reader keeps.
/// </summary>
[TestFixture]
public sealed class EquipmentAppearanceTests
{
    // Player 9 at revision 3 wears item.w as its weapon and item.c as accessory 2, and nothing else.
    private static readonly byte[] GoldenBytes =
    {
        0x31, 0x80,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77,
        0x00, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x63
    };

    private static readonly ItemDefinitionId Sword = new("item.w");
    private static readonly ItemDefinitionId Charm = new("item.c");

    public static EquipmentAppearance Golden =>
        new(new EntityId(9), 3, new[] { Sword, default, default, default, default, default, default, Charm });

    private static byte[] Encode(EquipmentAppearance message)
    {
        byte[] bytes = new byte[message.GetEncodedLength()];
        Assert.That(message.Write(bytes), Is.EqualTo(bytes.Length));
        return bytes;
    }

    private static bool Read(byte[] bytes)
    {
        return EquipmentAppearance.TryRead(bytes, out _);
    }

    [Test]
    public void Constructor_RefusesWhatTheReaderRefuses()
    {
        var eight = new ItemDefinitionId[8];
        Action noOne = () => _ = new EquipmentAppearance(default, 1, eight);
        Action seven = () => _ = new EquipmentAppearance(new EntityId(1), 1, eight.Take(7).ToArray());

        Assert.That(noOne, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(seven, Throws.ArgumentException);
    }

    [Test]
    public void Golden_WritesAndReads_ItsBytes_AndEachPositionsItem()
    {
        Assert.That(Encode(Golden), Is.EqualTo(GoldenBytes));
        Assert.That(EquipmentAppearance.TryRead(GoldenBytes, out EquipmentAppearance? read), Is.True);
        Assert.That((read!.Entity.Value, read.Revision), Is.EqualTo((9L, 3u)));
        Assert.That(read.Items, Is.EqualTo(Golden.Items));
        Assert.That(
            (read.ItemIn(EquipmentSlot.Weapon), read.ItemIn(EquipmentSlot.Accessory2),
                read.ItemIn(EquipmentSlot.Shield)),
            Is.EqualTo((Sword, Charm, default(ItemDefinitionId))));
        WireMatrix.AssertRejectsEveryTruncation(GoldenBytes, Read);
        WireMatrix.AssertRejectsTrailingData(GoldenBytes, Read);
        WireMatrix.AssertRejectsOtherOpcodes(GoldenBytes, Read);
    }

    [Test]
    public void Reader_RefusesAPlayerOf0_AndAnIdThatIsNoItems()
    {
        Assert.That(Read(WireMatrix.With(GoldenBytes, 2, 0x00)), Is.False, "a player of 0");
        Assert.That(Read(WireMatrix.With(GoldenBytes, 16, (byte)'s')), Is.False, "a skill's ID as the weapon");
        Assert.That(Read(WireMatrix.With(GoldenBytes, 41, (byte)'C')), Is.False, "a capital letter in accessory 2's");
    }

    // Network Protocol §6: 30 bytes with nothing worn, 542 with eight IDs of the longest.
    [Test]
    public void Sizes_AreThirtyBytesBare_And542AtMost()
    {
        var bare = new EquipmentAppearance(new EntityId(1), 0, new ItemDefinitionId[8]);
        ItemDefinitionId[] longest = Enumerable.Range(0, 8)
            .Select(index =>
                new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}"))
            .ToArray();
        var fullest = new EquipmentAppearance(new EntityId(long.MaxValue), uint.MaxValue, longest);

        Assert.That(
            (bare.GetEncodedLength(), EquipmentAppearance.MinEncodedLength),
            Is.EqualTo((30, 30)));
        Assert.That(
            (fullest.GetEncodedLength(), EquipmentAppearance.MaxEncodedLength),
            Is.EqualTo((542, 542)));
        Assert.That(EquipmentAppearance.TryRead(Encode(fullest), out EquipmentAppearance? read), Is.True);
        Assert.That(read!.Items, Is.EqualTo(longest));
        Assert.That(EquipmentAppearance.TryRead(Encode(bare), out EquipmentAppearance? empty), Is.True);
        Assert.That(empty!.Items, Is.All.EqualTo(default(ItemDefinitionId)));
    }
}
}
