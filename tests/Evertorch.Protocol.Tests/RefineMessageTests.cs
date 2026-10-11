using System;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 41, the Smith (Network Protocol §6): the refine command, a refiner's table, and the outcome to the
///     refiner, pinned by their golden bytes, their largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class RefineMessageTests
{
    // Row 42 from +6 at NPC 7, sequence 4.
    private static readonly byte[] ItemBytes =
    {
        0x2D, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06,
        0x04, 0x00, 0x00, 0x00
    };

    // NPC 7's first table: one item.b an attempt, destroyed on a failure; +1 at 100 % and +2 at 60 %, 20 coins each;
    // item.a names it.
    private static readonly byte[] TableBytes =
    {
        0x2D, 0x80,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x62,
        0x01, 0x01, 0x00,
        0x02,
        0x64, 0x14, 0x00, 0x00, 0x00,
        0x3C, 0x14, 0x00, 0x00, 0x00,
        0x01,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61
    };

    // Row 42, item.a, refined from +6 to +7.
    private static readonly byte[] OutcomeBytes =
    {
        0x2E, 0x80,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x06, 0x07, 0x01
    };

    private const int Failure = 20;
    private const int LevelsLowered = 21;
    private const int LevelCount = 22;
    private const int FirstChance = 23;
    private const int ItemCount = 33;

    private static readonly ItemDefinitionId ItemA = new("item.a");
    private static readonly ItemDefinitionId ItemB = new("item.b");
    private static readonly ItemDefinitionId LongestItem = new("item." + new string('a', 59));

    public static RefineItem ItemGolden => new(new EntityId(7), 42, 6, 4);

    public static RefineTable TableGolden => new(
        new EntityId(7),
        0,
        ItemB,
        1,
        RefineFailure.Destroy,
        0,
        new[] { new RefineTableStep(100, 20), new RefineTableStep(60, 20) },
        new[] { ItemA });

    public static RefineOutcome OutcomeGolden => new(42, ItemA, 6, 7, RefineOutcomeKind.Refined);

    public static RefineTable TableLargest => new(
        new EntityId(long.MaxValue),
        byte.MaxValue,
        LongestItem,
        byte.MaxValue,
        RefineFailure.Downgrade,
        9,
        Enumerable.Repeat(new RefineTableStep(100, uint.MaxValue), RefineTable.MaxLevels).ToArray(),
        Enumerable.Repeat(LongestItem, RefineTable.MaxItems).ToArray());

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static byte[] Changed(byte[] golden, int at, params byte[] values)
    {
        byte[] bytes = (byte[])golden.Clone();
        values.CopyTo(bytes, at);
        return bytes;
    }

    [TestCase(0x01, 0x06, 0x08, "a refine of two levels")]
    [TestCase(0x01, 0x06, 0x06, "a refine that stays")]
    [TestCase(0x02, 0x06, 0x01, "a destroyed row with a level after")]
    [TestCase(0x03, 0x06, 0x06, "a lowered row that does not fall")]
    [TestCase(0x03, 0x06, 0x07, "a lowered row that rises")]
    [TestCase(0x04, 0x06, 0x05, "an unchanged row whose levels differ")]
    [TestCase(0x00, 0x06, 0x07, "no outcome")]
    [TestCase(0x05, 0x06, 0x07, "a fifth outcome")]
    public void Outcome_OutOfItsRules_IsMalformed(int outcome, int before, int after, string reason)
    {
        byte[] bytes = Changed(OutcomeBytes, 18, (byte)before, (byte)after, (byte)outcome);

        Assert.That(RefineOutcome.TryRead(bytes, out _), Is.False, reason);
    }

    [Test]
    public void Command_WithoutAnNpcOrARow_IsMalformed()
    {
        Assert.That(RefineItem.TryRead(Changed(ItemBytes, 2, new byte[8]), out _), Is.False, "no NPC");
        Assert.That(RefineItem.TryRead(Changed(ItemBytes, 10, new byte[8]), out _), Is.False, "no row");
        Assert.That(RefineItem.TryRead(ItemBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
        Assert.That(RefineItem.TryRead(ItemBytes.Take(22).ToArray(), out _), Is.False);
    }

    [Test]
    public void Command_WriteAndRead_MatchItsGoldenBytes()
    {
        Assert.That(Encode(RefineItem.EncodedLength, bytes => ItemGolden.Write(bytes)), Is.EqualTo(ItemBytes));
        Assert.That(RefineItem.EncodedLength, Is.EqualTo(23));
        Assert.That(RefineItem.TryRead(ItemBytes, out RefineItem? read), Is.True);
        Assert.That(
            (read!.Npc, read.InventoryItem, read.RefineLevel, read.CommandSequence),
            Is.EqualTo((new EntityId(7), 42L, (byte)6, 4u)));
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        Action npcless = () => _ = new RefineItem(default, 42, 0, 1);
        Action rowless = () => _ = new RefineItem(new EntityId(7), 0, 0, 1);
        RefineTableStep[] one = { new(100, 20) };
        Action oreless = () => _ = new RefineTable(new EntityId(7), 0, ItemB, 0, RefineFailure.Destroy, 0, one,
            new[] { ItemA });
        Action flat = () => _ = new RefineTable(new EntityId(7), 0, ItemB, 1, RefineFailure.Downgrade, 0, one,
            new[] { ItemA });
        Action itemless = () => _ = new RefineTable(new EntityId(7), 0, ItemB, 1, RefineFailure.Destroy, 0, one,
            Array.Empty<ItemDefinitionId>());
        Action levelless = () => _ = new RefineTable(new EntityId(7), 0, ItemB, 1, RefineFailure.Destroy, 0,
            Array.Empty<RefineTableStep>(), new[] { ItemA });
        Action certain = () => _ = new RefineTable(new EntityId(7), 0, ItemB, 1, RefineFailure.Destroy, 0,
            new[] { new RefineTableStep(101, 20) }, new[] { ItemA });
        Action twoUp = () => _ = new RefineOutcome(42, ItemA, 6, 8, RefineOutcomeKind.Refined);
        Action unnamed = () => _ = new RefineOutcome(42, default, 6, 7, RefineOutcomeKind.Refined);

        Assert.That(npcless, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(rowless, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(oreless, Throws.ArgumentException);
        Assert.That(flat, Throws.ArgumentException);
        Assert.That(itemless, Throws.ArgumentException);
        Assert.That(levelless, Throws.ArgumentException);
        Assert.That(certain, Throws.ArgumentException);
        Assert.That(twoUp, Throws.ArgumentException);
        Assert.That(unnamed, Throws.ArgumentException);
    }

    [Test]
    public void Outcome_OfEachKind_RoundTrips_AndWithoutARow_IsMalformed()
    {
        Assert.That(RefineOutcome.TryRead(Changed(OutcomeBytes, 18, 0x06, 0x00, 0x02), out _), Is.True, "destroyed");
        Assert.That(RefineOutcome.TryRead(Changed(OutcomeBytes, 18, 0x06, 0x05, 0x03), out _), Is.True, "lowered");
        Assert.That(RefineOutcome.TryRead(Changed(OutcomeBytes, 18, 0x06, 0x06, 0x04), out _), Is.True, "unchanged");
        Assert.That(RefineOutcome.TryRead(Changed(OutcomeBytes, 2, new byte[8]), out _), Is.False, "no row");
        Assert.That(RefineOutcome.TryRead(Changed(OutcomeBytes, 12, 0x6E), out _), Is.False, "an item of no kind");
        Assert.That(RefineOutcome.TryRead(OutcomeBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    [Test]
    public void Outcome_WriteAndRead_MatchItsGoldenBytes_AndIsAtMost79Bytes()
    {
        Assert.That(
            Encode(OutcomeGolden.GetEncodedLength(), bytes => OutcomeGolden.Write(bytes)),
            Is.EqualTo(OutcomeBytes));
        Assert.That(RefineOutcome.TryRead(OutcomeBytes, out RefineOutcome? read), Is.True);
        Assert.That(
            (read!.InventoryItem, read.Item, read.LevelBefore, read.LevelAfter, read.Outcome),
            Is.EqualTo((42L, ItemA, (byte)6, (byte)7, RefineOutcomeKind.Refined)));
        Assert.That(RefineOutcome.MaxEncodedLength, Is.EqualTo(79));
        Assert.That(
            new RefineOutcome(1, LongestItem, 9, 0, RefineOutcomeKind.Destroyed).GetEncodedLength(),
            Is.EqualTo(RefineOutcome.MaxEncodedLength));
    }

    [Test]
    public void Table_AtItsLargest_Is132BytesAnd66AnItem_AndFitsOneReliableMessage()
    {
        byte[] bytes = Encode(TableLargest.GetEncodedLength(), bytes => TableLargest.Write(bytes));

        Assert.That(RefineTable.HeaderLength, Is.EqualTo(132));
        Assert.That(RefineTable.ItemLength, Is.EqualTo(66));
        Assert.That(bytes.Length, Is.EqualTo(132 + 66 * 13));
        Assert.That(bytes.Length, Is.LessThanOrEqualTo(NpcServices.MaxEncodedLength));
        Assert.That(RefineTable.TryRead(bytes, out RefineTable? read), Is.True);
        Assert.That((read!.OnFailure, read.LevelsLowered, read.Items.Count), Is.EqualTo((RefineFailure.Downgrade,
            (byte)9, 13)));
    }

    [Test]
    public void Table_OutOfItsRules_IsMalformed()
    {
        Assert.That(RefineTable.TryRead(Changed(TableBytes, 2, new byte[8]), out _), Is.False, "no NPC");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, 19, 0x00), out _), Is.False, "no ore");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, Failure, 0x00), out _), Is.False, "no failure");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, Failure, 0x04), out _), Is.False, "a fourth failure");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, Failure, 0x02), out _), Is.False, "a downgrade of 0");
        Assert.That(
            RefineTable.TryRead(Changed(TableBytes, Failure, 0x02, 0x0A), out _),
            Is.False,
            "a downgrade of ten levels");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, Failure, 0x02, 0x01), out _), Is.True,
            "a downgrade of one");
        Assert.That(
            RefineTable.TryRead(Changed(TableBytes, LevelsLowered, 0x01), out _),
            Is.False,
            "a destroy that lowers");
        Assert.That(
            RefineTable.TryRead(Changed(TableBytes, Failure, 0x03, 0x01), out _),
            Is.False,
            "a keep that lowers");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, LevelCount, 0x00), out _), Is.False, "no level");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, LevelCount, 0x0B), out _), Is.False, "eleven levels");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, FirstChance, 0x00), out _), Is.False, "a chance of 0");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, FirstChance, 0x65), out _), Is.False, "a chance of 101");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, ItemCount, 0x00), out _), Is.False, "no item");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, ItemCount, 0x0E), out _), Is.False, "fourteen items");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, 36, 0x6E), out _), Is.False, "an item of no kind");
        Assert.That(RefineTable.TryRead(Changed(TableBytes, 13, 0x58), out _), Is.False, "an ore of no kind");
        Assert.That(RefineTable.TryRead(TableBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    [Test]
    public void Table_WriteAndRead_MatchItsGoldenBytes()
    {
        Assert.That(
            Encode(TableGolden.GetEncodedLength(), bytes => TableGolden.Write(bytes)),
            Is.EqualTo(TableBytes));
        Assert.That(RefineTable.TryRead(TableBytes, out RefineTable? read), Is.True);
        Assert.That(
            (read!.Npc, read.Index, read.Ore, read.OreQuantity, read.OnFailure, read.LevelsLowered),
            Is.EqualTo((new EntityId(7), (byte)0, ItemB, (byte)1, RefineFailure.Destroy, (byte)0)));
        Assert.That(
            read.Steps.Select(step => (step.Chance, step.Coins)),
            Is.EqualTo(new[] { ((byte)100, 20u), ((byte)60, 20u) }));
        Assert.That(read.Items, Is.EqualTo(new[] { ItemA }));
    }
}
}
