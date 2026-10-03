using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Protocol 38, the account's storage (Network Protocol §6): three commands at the Storekeeper and two messages to
///     the character, pinned by their golden bytes, their largest sizes, and every rule their readers keep.
/// </summary>
[TestFixture]
public sealed class StorageMessageTests
{
    private static readonly byte[] OpenBytes =
    {
        0x26, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00
    };

    private static readonly byte[] DepositBytes =
    {
        0x27, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0A, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00
    };

    private static readonly byte[] WithdrawBytes =
    {
        0x28, 0x00,
        0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00,
        0x05, 0x00, 0x00, 0x00
    };

    // Revision 3, a fee of 20, the only part, ten of item.a in storage row 9.
    private static readonly byte[] SnapshotBytes =
    {
        0x28, 0x80,
        0x03, 0x00, 0x00, 0x00,
        0x14, 0x00, 0x00, 0x00,
        0x00,
        0x01,
        0x01,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x0A, 0x00, 0x00, 0x00,
        0x00
    };

    // From revision 3 to 4, storage row 9 emptied.
    private static readonly byte[] ChangedBytes =
    {
        0x29, 0x80,
        0x03, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x00, 0x00, 0x00, 0x00,
        0x00
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");

    public static StorageOpen OpenGolden => new(new EntityId(7), 3);

    public static StorageDeposit DepositGolden => new(new EntityId(7), 42, 10, 4);

    public static StorageWithdraw WithdrawGolden => new(new EntityId(7), 5, 1, 5);

    public static StorageSnapshot SnapshotGolden => new(3, 20, 0, 1, new[] { new StorageEntry(9, ItemA, 10) });

    public static StorageChanged ChangedGolden => new(3, 4, new StorageEntry(9, ItemA, 0));

    private static byte[] Encode(int length, Func<byte[], int> write)
    {
        byte[] bytes = new byte[length];
        Assert.That(write(bytes), Is.EqualTo(length));
        return bytes;
    }

    private static StorageEntry Longest(long row)
    {
        return new StorageEntry(row, new ItemDefinitionId("item." + new string('a', 59)), 1_000_000);
    }

    [TestCase(10)]
    [TestCase(18)]
    public void DepositAndWithdraw_WithoutARowOrAQuantity_AreMalformed(int at)
    {
        byte[] deposit = (byte[])DepositBytes.Clone();
        byte[] withdraw = (byte[])WithdrawBytes.Clone();
        new byte[4].CopyTo(deposit, at);
        new byte[4].CopyTo(withdraw, at);

        Assert.That(StorageDeposit.TryRead(deposit, out _), Is.False);
        Assert.That(StorageWithdraw.TryRead(withdraw, out _), Is.False);
    }

    [Test]
    public void Changed_OutOfItsRules_IsMalformed_AndARowOfOneMayBeRefined()
    {
        var refined = new StorageChanged(3, 4, new StorageEntry(9, ItemA, 1, 7));
        byte[] bytes = Encode(refined.GetEncodedLength(), written => refined.Write(written));
        byte[] stack = (byte[])bytes.Clone();
        stack[26] = 2;
        byte[] emptied = (byte[])bytes.Clone();
        emptied[26] = 0;

        Assert.That(StorageChanged.TryRead(bytes, out StorageChanged? read), Is.True);
        Assert.That(read!.Row.RefineLevel, Is.EqualTo((byte)7));
        Assert.That(StorageChanged.TryRead(stack, out _), Is.False, "a refined stack");
        Assert.That(StorageChanged.TryRead(emptied, out _), Is.False, "a refined row emptied");
        Assert.That(StorageChanged.TryRead(ChangedBytes.Take(ChangedBytes.Length - 1).ToArray(), out _), Is.False);
    }

    [Test]
    public void ClientMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(Encode(StorageOpen.EncodedLength, bytes => OpenGolden.Write(bytes)), Is.EqualTo(OpenBytes));
        Assert.That(
            Encode(StorageDeposit.EncodedLength, bytes => DepositGolden.Write(bytes)),
            Is.EqualTo(DepositBytes));
        Assert.That(
            Encode(StorageWithdraw.EncodedLength, bytes => WithdrawGolden.Write(bytes)),
            Is.EqualTo(WithdrawBytes));

        Assert.That(StorageOpen.TryRead(OpenBytes, out StorageOpen? open), Is.True);
        Assert.That((open!.Npc, open.CommandSequence), Is.EqualTo((new EntityId(7), 3u)));
        Assert.That(StorageDeposit.TryRead(DepositBytes, out StorageDeposit? deposit), Is.True);
        Assert.That(
            (deposit!.Npc, deposit.InventoryItem, deposit.Quantity, deposit.CommandSequence),
            Is.EqualTo((new EntityId(7), 42L, 10u, 4u)));
        Assert.That(StorageWithdraw.TryRead(WithdrawBytes, out StorageWithdraw? withdraw), Is.True);
        Assert.That(
            (withdraw!.Npc, withdraw.StorageItem, withdraw.Quantity, withdraw.CommandSequence),
            Is.EqualTo((new EntityId(7), 5L, 1u, 5u)));
    }

    [Test]
    public void Constructors_RefuseWhatTheReadersRefuse()
    {
        Action rowless = () => _ = new StorageDeposit(new EntityId(7), 0, 1, 1);
        Action nothing = () => _ = new StorageWithdraw(new EntityId(7), 1, 0, 1);
        Action dear = () => _ = new StorageSnapshot(
            0,
            (uint)ContentLimits.MaxDepositFee + 1,
            0,
            1,
            Array.Empty<StorageEntry>());
        Action pastItsCount = () => _ = new StorageSnapshot(0, 0, 1, 1, Array.Empty<StorageEntry>());
        StorageEntry[] thirteen = Enumerable.Range(1, 13).Select(row => new StorageEntry(row, ItemA, 1)).ToArray();
        Action tooMany = () => _ = new StorageSnapshot(0, 0, 0, 1, thirteen);

        Assert.That(rowless, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(nothing, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(dear, Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(pastItsCount, Throws.ArgumentException);
        Assert.That(tooMany, Throws.ArgumentException);
    }

    [Test]
    public void CreateParts_SplitsThreeHundredRowsIntoTwentyFive_AndAnEmptyStorageIntoOneEmptyPart()
    {
        StorageEntry[] rows = Enumerable.Range(1, 300).Select(row => new StorageEntry(row, ItemA, 1)).ToArray();

        IReadOnlyList<StorageSnapshot> parts = StorageSnapshot.CreateParts(8, 20, rows);
        IReadOnlyList<StorageSnapshot> empty = StorageSnapshot.CreateParts(0, 20, Array.Empty<StorageEntry>());

        Assert.That(parts.Select(part => part.Part), Is.EqualTo(Enumerable.Range(0, 25).Select(part => (byte)part)));
        Assert.That(parts.All(part => part.PartCount == 25 && part.Revision == 8 && part.DepositFee == 20), Is.True);
        Assert.That(parts.SelectMany(part => part.Entries).Select(entry => entry.StorageItem), Is.EqualTo(
            Enumerable.Range(1, 300).Select(row => (long)row)));
        Assert.That((empty.Count, empty[0].Entries.Count, empty[0].IsLast), Is.EqualTo((1, 0, true)));
    }

    [Test]
    public void ServerMessages_WriteAndRead_MatchTheirGoldenBytes()
    {
        Assert.That(
            Encode(SnapshotGolden.GetEncodedLength(), bytes => SnapshotGolden.Write(bytes)),
            Is.EqualTo(SnapshotBytes));
        Assert.That(
            Encode(ChangedGolden.GetEncodedLength(), bytes => ChangedGolden.Write(bytes)),
            Is.EqualTo(ChangedBytes));

        Assert.That(StorageSnapshot.TryRead(SnapshotBytes, out StorageSnapshot? snapshot), Is.True);
        Assert.That(
            (snapshot!.Revision, snapshot.DepositFee, snapshot.Part, snapshot.PartCount, snapshot.IsLast),
            Is.EqualTo((3u, 20u, (byte)0, (byte)1, true)));
        StorageEntry entry = snapshot.Entries.Single();
        Assert.That(
            (entry.StorageItem, entry.Item, entry.Quantity, entry.RefineLevel),
            Is.EqualTo((9L, ItemA, 10u, (byte)0)));
        Assert.That(StorageChanged.TryRead(ChangedBytes, out StorageChanged? changed), Is.True);
        Assert.That(
            (changed!.PriorRevision, changed.NewRevision, changed.Row.StorageItem, changed.Row.Quantity),
            Is.EqualTo((3u, 4u, 9L, 0u)));
    }

    [Test]
    public void Snapshot_OutOfItsRules_IsMalformed()
    {
        byte[] Changed(int at, params byte[] values)
        {
            byte[] bytes = (byte[])SnapshotBytes.Clone();
            values.CopyTo(bytes, at);
            return bytes;
        }

        Assert.That(StorageSnapshot.TryRead(Changed(6, 0x41, 0x42, 0x0F, 0x00), out _), Is.False,
            "a fee above 1,000,000");
        Assert.That(StorageSnapshot.TryRead(Changed(11, 0x00), out _), Is.False, "no parts");
        Assert.That(StorageSnapshot.TryRead(Changed(10, 0x01), out _), Is.False, "a part past its count");
        Assert.That(StorageSnapshot.TryRead(Changed(12, 0x0D), out _), Is.False, "thirteen entries");
        Assert.That(StorageSnapshot.TryRead(Changed(29, 0x00), out _), Is.False, "a row of nothing");
        Assert.That(StorageSnapshot.TryRead(Changed(13, 0x00), out _), Is.False, "a row ID of 0");
        Assert.That(StorageSnapshot.TryRead(Changed(33, 0x01), out _), Is.False, "a refined stack of ten");
        Assert.That(StorageSnapshot.TryRead(SnapshotBytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
    }

    // Twelve rows with the longest IDs fill a part to 961 bytes and a change to 89, each within one datagram's 1,020
    // (Network Protocol §6).
    [Test]
    public void TheLargestParts_FitOneDatagram()
    {
        StorageEntry[] rows = Enumerable.Range(1, StorageSnapshot.MaxEntries).Select(row => Longest(row)).ToArray();
        var snapshot = new StorageSnapshot(uint.MaxValue, ContentLimits.MaxDepositFee, 0, 1, rows);
        var changed = new StorageChanged(uint.MaxValue - 1, uint.MaxValue, Longest(1));

        Assert.That((snapshot.GetEncodedLength(), changed.GetEncodedLength()), Is.EqualTo((961, 89)));
        Assert.That(
            StorageSnapshot.TryRead(Encode(snapshot.GetEncodedLength(), bytes => snapshot.Write(bytes)), out _),
            Is.True);
        Assert.That(
            StorageChanged.TryRead(Encode(changed.GetEncodedLength(), bytes => changed.Write(bytes)), out _),
            Is.True);
    }
}
}
