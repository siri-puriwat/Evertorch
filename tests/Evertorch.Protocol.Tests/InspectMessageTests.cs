using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Inspection (Network Protocol §6): the asking, protocol 44, and the answer in parts, protocol 49, pinned by their
///     golden bytes, their largest sizes, the packing of the eight positions, and every rule their readers keep.
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

    // Player 9, answering command 0x0A0B0C0D at revision 3, part 1 of 1: item.w at +7 holding item.k as the weapon,
    // and the seven other positions empty.
    private static readonly byte[] EquipmentBytes =
    {
        0x30, 0x80,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0D, 0x0C, 0x0B, 0x0A,
        0x03, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x08,
        0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x77, 0x07,
        0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B,
        0x02, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x00, 0x00, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00, 0x00
    };

    private const int Header = 21;
    private const int EntryCount = 20;
    private const int SecondEntry = 40;

    private static readonly ItemDefinitionId Sword = new("item.w");
    private static readonly ItemDefinitionId Keen = new("item.k");

    public static InspectPlayer InspectGolden => new(new EntityId(9), 3);

    public static InspectedEquipment EquipmentGolden =>
        new(new EntityId(9), 0x0A0B0C0D, 3, 0, 1, Entries(new InspectedItem(Sword, 7, new[] { Keen })));

    private static InspectedEntry[] Entries(InspectedItem weapon, InspectedItem other = default)
    {
        return EquipmentPositions.All
            .Select(slot => new InspectedEntry(slot, slot == EquipmentSlot.Weapon ? weapon : other))
            .ToArray();
    }

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Encode(InspectedEquipment part)
    {
        return Encode(part.GetEncodedLength(), bytes => part.Write(bytes));
    }

    private static ItemDefinitionId Longest(int index)
    {
        return new ItemDefinitionId($"item.{(char)('a' + index)}{new string('c', DefinitionIdLimits.MaxLength - 6)}");
    }

    private static bool ReadAny(byte[] bytes)
    {
        return InspectPlayer.TryRead(bytes, out _) || InspectedEquipment.TryRead(bytes, out _);
    }

    private static bool ReadPart(byte[] bytes)
    {
        return InspectedEquipment.TryRead(bytes, out _);
    }

    private static bool ThrowsArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        InspectedEntry[] eight = Entries(default);
        Action noPlayer = () => _ = new InspectPlayer(default, 1);
        Action noOne = () => _ = new InspectedEquipment(default, 1, 1, 0, 1, eight);
        Action pastItsCount = () => _ = new InspectedEquipment(new EntityId(1), 1, 1, 1, 1, eight);
        Action nineParts = () => _ = new InspectedEquipment(new EntityId(1), 1, 1, 0, 9, eight);
        Action noEntry = () => _ = new InspectedEquipment(new EntityId(1), 1, 1, 0, 1, Array.Empty<InspectedEntry>());
        Action twice = () => _ = new InspectedEquipment(new EntityId(1), 1, 1, 0, 1, new[] { eight[0], eight[0] });
        Action noPosition = () => _ = new InspectedEntry(EquipmentSlot.None, default);
        Action levelled = () => _ = new InspectedItem(default, 1);
        Action carded = () => _ = new InspectedItem(default, 0, new[] { Keen });
        Action five = () => _ = new InspectedItem(Sword, 0, new[] { Keen, Keen, Keen, Keen, Keen });

        Assert.That(noPlayer, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(noOne, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(new[] { pastItsCount, nineParts, noEntry, twice }, Has.All.Matches<Action>(ThrowsArgument));
        Assert.That(noPosition, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(levelled, Throws.ArgumentException);
        Assert.That(carded, Throws.ArgumentException);
        Assert.That(five, Throws.ArgumentException);
    }

    [Test]
    public void CreateParts_OfNothingWorn_IsOnePartOfEightEmptyEntries()
    {
        IReadOnlyList<InspectedEquipment> parts =
            InspectedEquipment.CreateParts(new EntityId(4), 7, 2, Entries(default));

        Assert.That(parts, Has.Count.EqualTo(1));
        Assert.That(parts[0].GetEncodedLength(), Is.EqualTo(Header + 8 * 5));
        Assert.That(parts[0].Entries.All(entry => entry.Item.IsEmpty), Is.True);
    }

    // Network Protocol §6: content's own worst case, a weapon with its four slots carded and seven pieces with one card
    // each, every ID of the longest, takes two parts, of 894 and 426 bytes.
    [Test]
    public void CreateParts_OfTheContentsWorstCase_TakesTwoParts()
    {
        InspectedEntry[] worn = EquipmentPositions.All
            .Select(slot => new InspectedEntry(
                slot,
                new InspectedItem(
                    Longest(5),
                    byte.MaxValue,
                    Enumerable.Repeat(Longest(0), slot == EquipmentSlot.Weapon ? ProtocolLimits.MaxCardsPerItem : 1)
                        .ToArray())))
            .ToArray();

        IReadOnlyList<InspectedEquipment> parts = InspectedEquipment.CreateParts(new EntityId(4), 7, 2, worn);

        Assert.That(parts.Select(part => part.GetEncodedLength()), Is.EqualTo(new[] { 894, 426 }));
        Assert.That(parts.Select(part => part.Entries.Count), Is.EqualTo(new[] { 5, 3 }));
    }

    // Network Protocol §6: the wire's worst case, eight entries of the longest IDs with four cards each, takes three
    // parts, the first two of exactly 1,020 bytes; every part reads back, and the positions keep their order.
    [Test]
    public void CreateParts_OfTheWiresWorstCase_TakesThreeParts_TheFirstTwoFull()
    {
        ItemDefinitionId[] cards = Enumerable.Range(0, ProtocolLimits.MaxCardsPerItem).Select(Longest).ToArray();
        InspectedEntry[] worn = EquipmentPositions.All
            .Select(slot => new InspectedEntry(slot, new InspectedItem(Longest(5), byte.MaxValue, cards)))
            .ToArray();

        IReadOnlyList<InspectedEquipment> parts = InspectedEquipment.CreateParts(new EntityId(4), 7, 2, worn);

        Assert.That(parts.Select(part => part.GetEncodedLength()), Is.EqualTo(new[] { 1020, 1020, 687 }));
        Assert.That(parts.Select(part => part.Entries.Count), Is.EqualTo(new[] { 3, 3, 2 }));
        Assert.That(parts.Select(part => (part.Part, part.PartCount)),
            Is.EqualTo(new (byte, byte)[] { (0, 3), (1, 3), (2, 3) }));
        var read = new List<InspectedEntry>();
        foreach (InspectedEquipment part in parts)
        {
            Assert.That(InspectedEquipment.TryRead(Encode(part), out InspectedEquipment? back), Is.True);
            Assert.That((back!.Entity.Value, back.CommandSequence, back.Revision), Is.EqualTo((4L, 7u, 2u)));
            read.AddRange(back.Entries);
        }

        Assert.That(read.Select(entry => entry.Slot), Is.EqualTo(EquipmentPositions.All));
        Assert.That(read.All(entry => entry.Item.Cards.SequenceEqual(cards)), Is.True);
    }

    [Test]
    public void Messages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(InspectPlayer.EncodedLength, bytes => InspectGolden.Write(bytes)), Is.EqualTo(InspectBytes));
        Assert.That(Encode(EquipmentGolden), Is.EqualTo(EquipmentBytes));

        Assert.That(InspectPlayer.TryRead(InspectBytes, out InspectPlayer? inspect), Is.True);
        Assert.That((inspect!.Entity.Value, inspect.CommandSequence), Is.EqualTo((9L, 3u)));
        Assert.That(InspectedEquipment.TryRead(EquipmentBytes, out InspectedEquipment? shown), Is.True);
        Assert.That(
            (shown!.Entity.Value, shown.CommandSequence, shown.Revision, shown.Part, shown.PartCount),
            Is.EqualTo((9L, 0x0A0B0C0Du, 3u, (byte)0, (byte)1)));
        InspectedItem weapon = shown.Entries[0].Item;
        Assert.That((weapon.Item, weapon.RefineLevel, weapon.Cards.Single()), Is.EqualTo((Sword, (byte)7, Keen)));
        Assert.That(shown.Entries.Select(entry => entry.Slot), Is.EqualTo(EquipmentPositions.All));
        Assert.That(shown.Entries.Skip(1).All(entry => entry.Item.IsEmpty), Is.True);
        foreach (byte[] golden in new[] { InspectBytes, EquipmentBytes })
        {
            WireMatrix.AssertRejectsEveryTruncation(golden, ReadAny);
            WireMatrix.AssertRejectsTrailingData(golden, ReadAny);
        }

        WireMatrix.AssertRejectsOtherOpcodes(InspectBytes, bytes => InspectPlayer.TryRead(bytes, out _));
        WireMatrix.AssertRejectsOtherOpcodes(EquipmentBytes, ReadPart);
    }

    [Test]
    public void Readers_RefuseWhatBreaksARule()
    {
        byte[] card = EquipmentBytes.Skip(32).Take(8).ToArray();
        byte[] fiveCards = EquipmentBytes.Take(31)
            .Append((byte)0x05)
            .Concat(Enumerable.Repeat(card, 5).SelectMany(bytes => bytes))
            .Concat(EquipmentBytes.Skip(SecondEntry))
            .ToArray();
        byte[] cardOnEmpty = EquipmentBytes.Take(SecondEntry + 4)
            .Concat(new byte[] { 0x01, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x6B })
            .Concat(EquipmentBytes.Skip(SecondEntry + 5))
            .ToArray();

        Assert.That(InspectPlayer.TryRead(WireMatrix.With(InspectBytes, 2, 0x00), out _), Is.False, "a player of 0");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, 2, 0x00)), Is.False, "a player of 0");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, 18, 0x01)), Is.False, "a part at its count");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, 19, 0x00)), Is.False, "a count of 0");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, 18, 0x08, 0x09)), Is.False, "a count of 9");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, EntryCount, 0x00)), Is.False, "no entry");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, SecondEntry, 0x00)), Is.False, "a slot of 0");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, SecondEntry, 0x09)), Is.False, "a slot of 9");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, SecondEntry, 0x01)), Is.False, "the weapon twice");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, 24, (byte)'s')), Is.False, "no item");
        Assert.That(ReadPart(fiveCards), Is.False, "five cards");
        Assert.That(ReadPart(WireMatrix.With(EquipmentBytes, SecondEntry + 3, 0x01)), Is.False,
            "a level on an empty one");
        Assert.That(ReadPart(cardOnEmpty), Is.False, "a card on an empty one");
    }

    [Test]
    public void TheAskingKeepsItsSize()
    {
        Assert.That(InspectPlayer.EncodedLength, Is.EqualTo(14));
    }
}
}
