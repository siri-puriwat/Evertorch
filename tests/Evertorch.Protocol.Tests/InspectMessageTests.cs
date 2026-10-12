using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 44, inspection (Network Protocol §6): the asking and the answer, pinned by their golden bytes, their
///     largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class InspectMessageTests
{
    // Player 9, command 3.
    private static readonly byte[] InspectBytes =
    {
        0x30, 0x00,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00
    };

    // Player 9 wears item.w at +7 holding item.k, and no armor.
    private static readonly byte[] EquipmentBytes =
    {
        0x30, 0x80,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77, 0x07,
        0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B,
        0x00, 0x00, 0x00,
        0x00
    };

    private static readonly ItemDefinitionId Sword = new("item.w");
    private static readonly ItemDefinitionId Keen = new("item.k");

    public static InspectPlayer InspectGolden => new(new EntityId(9), 3);

    public static InspectedEquipment EquipmentGolden =>
        new(new EntityId(9), new InspectedItem(Sword, 7, new[] { Keen }), default);

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static ItemDefinitionId Longest(int index)
    {
        return new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}");
    }

    private static bool ReadAny(byte[] bytes)
    {
        return InspectPlayer.TryRead(bytes, out _) || InspectedEquipment.TryRead(bytes, out _);
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        Action noPlayer = () => _ = new InspectPlayer(default, 1);
        Action noOne = () => _ = new InspectedEquipment(default, default, default);
        Action levelled = () => _ = new InspectedItem(default, 1);
        Action carded = () => _ = new InspectedItem(default, 0, new[] { Keen });
        Action five = () => _ = new InspectedItem(Sword, 0, new[] { Keen, Keen, Keen, Keen, Keen });

        Assert.That(noPlayer, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noOne, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(levelled, Throws.ArgumentException);
        Assert.That(carded, Throws.ArgumentException);
        Assert.That(five, Throws.ArgumentException);
        Assert.That((default(InspectedItem).IsEmpty, default(InspectedItem).Cards),
            Is.EqualTo((true, Array.Empty<ItemDefinitionId>())));
    }

    [Test]
    public void Messages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(InspectPlayer.EncodedLength, bytes => InspectGolden.Write(bytes)), Is.EqualTo(InspectBytes));
        Assert.That(
            Encode(EquipmentGolden.GetEncodedLength(), bytes => EquipmentGolden.Write(bytes)),
            Is.EqualTo(EquipmentBytes));

        Assert.That(InspectPlayer.TryRead(InspectBytes, out InspectPlayer? inspect), Is.True);
        Assert.That((inspect!.Entity.Value, inspect.CommandSequence), Is.EqualTo((9L, 3u)));
        Assert.That(InspectedEquipment.TryRead(EquipmentBytes, out InspectedEquipment? shown), Is.True);
        Assert.That(
            (shown!.Entity.Value, shown.Weapon.Item, shown.Weapon.RefineLevel, shown.Weapon.Cards.Single()),
            Is.EqualTo((9L, Sword, (byte)7, Keen)));
        Assert.That((shown.Armor.IsEmpty, shown.Armor.RefineLevel, shown.Armor.Cards.Count),
            Is.EqualTo((true, (byte)0, 0)));
        foreach (byte[] golden in new[] { InspectBytes, EquipmentBytes })
        {
            WireMatrix.AssertRejectsEveryTruncation(golden, bytes => ReadAny(bytes));
            WireMatrix.AssertRejectsTrailingData(golden, bytes => ReadAny(bytes));
        }

        WireMatrix.AssertRejectsOtherOpcodes(InspectBytes, bytes => InspectPlayer.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(EquipmentBytes, bytes => InspectedEquipment.TryRead(bytes, out _));
    }

    [Test]
    public void Readers_RefuseWhatBreaksARule()
    {
        byte[] cardOnEmpty = EquipmentBytes.Take(28)
            .Concat(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B })
            .ToArray();

        Assert.That(InspectPlayer.TryRead(WireMatrix.With(InspectBytes, 2, 0x00), out _), Is.False, "a player of 0");
        Assert.That(InspectedEquipment.TryRead(WireMatrix.With(EquipmentBytes, 2, 0x00), out _), Is.False,
            "a player of 0");
        Assert.That(InspectedEquipment.TryRead(WireMatrix.With(EquipmentBytes, 12, (byte)'s'), out _), Is.False,
            "no item");
        Assert.That(InspectedEquipment.TryRead(WireMatrix.With(EquipmentBytes, 19, 0x05), out _), Is.False,
            "five cards");
        Assert.That(
            InspectedEquipment.TryRead(WireMatrix.With(EquipmentBytes, 30, 0x01), out _),
            Is.False,
            "a level on an empty slot");
        Assert.That(InspectedEquipment.TryRead(cardOnEmpty, out _), Is.False, "a card on an empty slot");
    }

    // Network Protocol §6: an inspection is 14 bytes and its answer at most 674, two items of the longest ID with four
    // cards each.
    [Test]
    public void TheLargestMessages_KeepTheirSizes()
    {
        ItemDefinitionId[] cards = Enumerable.Range(0, ProtocolLimits.MaxCardsPerItem).Select(Longest).ToArray();
        var item = new InspectedItem(Longest(5), byte.MaxValue, cards);
        var largest = new InspectedEquipment(new EntityId(long.MaxValue), item, item);

        Assert.That(InspectPlayer.EncodedLength, Is.EqualTo(14));
        Assert.That((largest.GetEncodedLength(), InspectedEquipment.MaxEncodedLength), Is.EqualTo((674, 674)));
        byte[] written = Encode(largest.GetEncodedLength(), bytes => largest.Write(bytes));
        Assert.That(InspectedEquipment.TryRead(written, out InspectedEquipment? read), Is.True);
        Assert.That(read!.Armor.Cards, Is.EqualTo(cards));
    }
}
}
