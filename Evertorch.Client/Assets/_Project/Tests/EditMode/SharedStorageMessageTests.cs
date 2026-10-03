using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET golden bytes so both compilers and runtimes agree on the wire format.
[TestFixture]
public sealed class SharedStorageMessageTests
{
    private static readonly byte[] StorageOpenBytes =
    {
        0x26, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00
    };

    private static readonly byte[] StorageDepositBytes =
    {
        0x27, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0A, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00
    };

    private static readonly byte[] StorageWithdrawBytes =
    {
        0x28, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00
    };

    private static readonly byte[] StorageSnapshotBytes =
    {
        0x28, 0x80, 0x03, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x0A, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] StorageChangedBytes =
    {
        0x29, 0x80, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
        0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00, 0x69, 0x74, 0x65, 0x6D, 0x2E, 0x61,
        0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly ItemDefinitionId ItemA = new("item.a");

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    [Test]
    public void ClientMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var npc = new EntityId(7);

        Assert.That(Encode(StorageOpen.EncodedLength, new StorageOpen(npc, 3).Write), Is.EqualTo(StorageOpenBytes));
        Assert.That(
            Encode(StorageDeposit.EncodedLength, new StorageDeposit(npc, 42, 10, 4).Write),
            Is.EqualTo(StorageDepositBytes));
        Assert.That(
            Encode(StorageWithdraw.EncodedLength, new StorageWithdraw(npc, 5, 1, 5).Write),
            Is.EqualTo(StorageWithdrawBytes));
        Assert.That(StorageDeposit.TryRead(StorageDepositBytes, out StorageDeposit? deposit), Is.True);
        Assert.That((deposit!.InventoryItem, deposit.Quantity), Is.EqualTo((42L, 10u)));
        Assert.That(StorageWithdraw.TryRead(StorageWithdrawBytes, out StorageWithdraw? withdraw), Is.True);
        Assert.That((withdraw!.StorageItem, withdraw.Quantity), Is.EqualTo((5L, 1u)));
    }

    [Test]
    public void ServerMessages_WriteTheirGoldenBytes_AndReadThemBack()
    {
        var snapshot = new StorageSnapshot(3, 20, 0, 1, new[] { new StorageEntry(9, ItemA, 10) });
        var changed = new StorageChanged(3, 4, new StorageEntry(9, ItemA, 0));

        Assert.That(Encode(snapshot.GetEncodedLength(), snapshot.Write), Is.EqualTo(StorageSnapshotBytes));
        Assert.That(Encode(changed.GetEncodedLength(), changed.Write), Is.EqualTo(StorageChangedBytes));
        Assert.That(StorageSnapshot.TryRead(StorageSnapshotBytes, out StorageSnapshot? read), Is.True);
        Assert.That(
            (read!.Revision, read.DepositFee, read.IsLast, read.Entries.Single().Quantity),
            Is.EqualTo((3u, 20u, true, 10u)));
        Assert.That(StorageChanged.TryRead(StorageChangedBytes, out StorageChanged? change), Is.True);
        Assert.That((change!.NewRevision, change.Row.Quantity), Is.EqualTo((4u, 0u)));
    }

    // Twelve rows with the longest IDs fill a part to 961 bytes, within one datagram (Network Protocol §6).
    [Test]
    public void TheLargestPart_IsNineHundredSixtyOneBytes()
    {
        var longest = new ItemDefinitionId("item." + new string('a', 59));
        StorageEntry[] rows = Enumerable.Range(1, StorageSnapshot.MaxEntries)
            .Select(row => new StorageEntry(row, longest, 1_000_000))
            .ToArray();

        Assert.That(new StorageSnapshot(uint.MaxValue, 1_000_000, 0, 1, rows).GetEncodedLength(), Is.EqualTo(961));
    }
}
}
