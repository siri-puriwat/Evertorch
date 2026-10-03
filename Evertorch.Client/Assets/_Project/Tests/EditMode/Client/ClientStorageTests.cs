using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's view of the account's storage (Gameplay Systems §11.4; Prototype Content §2): a read taken whole,
///     changes applied only from the revision held, a refusal or a gap leaving it to be read again, and the words.
/// </summary>
[TestFixture]
public sealed class ClientStorageTests
{
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");
    private static readonly ItemDefinitionId Staff = new("item.weapon.ash_staff");

    private static ClientStorage Read(uint revision, params StorageEntry[] rows)
    {
        var storage = new ClientStorage();
        storage.BeginRead();
        foreach (StorageSnapshot part in StorageSnapshot.CreateParts(revision, 20, rows))
        {
            storage.OnSnapshot(part);
        }

        return storage;
    }

    [Test]
    public void AGap_OrACommitsRefusal_LeavesItToBeReadAgain_AndARefusedReadEndsTheWaitUntilAskedAnew()
    {
        ClientStorage gap = Read(3);
        ClientStorage refused = Read(3);
        ClientStorage full = Read(3);
        var waiting = new ClientStorage();
        waiting.BeginRead();

        gap.OnChanged(new StorageChanged(4, 5, new StorageEntry(9, Gel, 1)));
        refused.OnRefused(StorageCommand.Withdraw, CommandRejectionReason.InvalidTarget);
        full.OnRefused(StorageCommand.Deposit, CommandRejectionReason.InventoryFull);
        waiting.OnRefused(StorageCommand.Open, CommandRejectionReason.ServiceUnavailable);
        (bool, bool, bool) afterRefusal = (waiting.IsReading, waiting.IsCurrent, waiting.IsReadRefused);
        waiting.BeginRead();

        Assert.That((gap.IsCurrent, gap.Revision, gap.Rows.Count), Is.EqualTo((false, 3u, 0)));
        Assert.That((refused.IsCurrent, full.IsCurrent), Is.EqualTo((false, false)));
        Assert.That(afterRefusal, Is.EqualTo((false, false, true)));
        Assert.That((waiting.IsReading, waiting.IsReadRefused), Is.EqualTo((true, false)), "asked anew");
    }

    [Test]
    public void APartOutOfOrder_SpoilsTheRead()
    {
        StorageEntry[] rows = Enumerable.Range(1, 30).Select(row => new StorageEntry(row, Gel, 1)).ToArray();
        IReadOnlyList<StorageSnapshot> parts = StorageSnapshot.CreateParts(5, 20, rows);
        var storage = new ClientStorage();
        storage.BeginRead();

        storage.OnSnapshot(parts[0]);
        storage.OnSnapshot(parts[2]);

        Assert.That((storage.IsCurrent, storage.IsReading), Is.EqualTo((false, false)));
    }

    [Test]
    public void ARead_IsTakenOnceItsLastPartArrives()
    {
        StorageEntry[] rows = Enumerable.Range(1, 14).Select(row => new StorageEntry(row, Gel, 1)).ToArray();
        IReadOnlyList<StorageSnapshot> parts = StorageSnapshot.CreateParts(5, 20, rows);
        var storage = new ClientStorage();
        storage.BeginRead();

        storage.OnSnapshot(parts[0]);
        (bool, bool, int) halfway = (storage.IsCurrent, storage.IsReading, storage.Rows.Count);
        storage.OnSnapshot(parts[1]);

        Assert.That(halfway, Is.EqualTo((false, true, 0)));
        Assert.That(
            (storage.IsCurrent, storage.IsReading, storage.Rows.Count, storage.Revision, storage.DepositFee),
            Is.EqualTo((true, false, 14, 5u, 20u)));
    }

    // A second press refused while the first deposit goes through says nothing of storage: the first one's change
    // still applies and says what it moved.
    [Test]
    public void ARefusalThatSaysNothingOfStorage_LeavesItCurrent()
    {
        ClientStorage storage = Read(3, new StorageEntry(9, Gel, 6));
        var moved = new List<StorageDelta>();
        storage.ChangeApplied += moved.Add;
        int version = storage.Version;

        storage.OnRefused(StorageCommand.Deposit, CommandRejectionReason.ItemActionInFlight);
        storage.OnRefused(StorageCommand.Withdraw, CommandRejectionReason.OutOfRange);
        storage.OnChanged(new StorageChanged(3, 4, new StorageEntry(9, Gel, 10)));

        Assert.That((storage.IsCurrent, storage.Version), Is.EqualTo((true, version + 1)));
        Assert.That(moved.Single().Quantity, Is.EqualTo(4L));
    }

    [Test]
    public void Changes_FromTheRevisionHeld_Apply_AndSayWhatTheyMoved()
    {
        ClientStorage storage = Read(3, new StorageEntry(9, Gel, 6));
        var moved = new List<StorageDelta>();
        storage.ChangeApplied += moved.Add;

        storage.OnChanged(new StorageChanged(3, 4, new StorageEntry(9, Gel, 10)));
        storage.OnChanged(new StorageChanged(4, 5, new StorageEntry(12, Staff, 1)));
        storage.OnChanged(new StorageChanged(5, 6, new StorageEntry(9, Gel, 0)));

        Assert.That(
            moved.Select(delta => (delta.Item, delta.Quantity, delta.DepositFee)),
            Is.EqualTo(new[] { (Gel, 4L, 20u), (Staff, 1L, 20u), (Gel, -10L, 20u) }));
        Assert.That(
            storage.Rows.Select(row => (row.StorageItem, row.Quantity)),
            Is.EqualTo(new[] { (12L, 1u) }));
        Assert.That(storage.Revision, Is.EqualTo(6u));
    }

    // Two reads at once, then one a second: one stricter than the server's three (Network Protocol §11).
    [Test]
    public void TheReadBucket_TakesTwoAtOnce_ThenOneASecond()
    {
        var reads = new ChatThrottle(ChatThrottle.ReadBurst);

        bool[] atOnce = { reads.TryTake(10.0), reads.TryTake(10.0), reads.TryTake(10.0) };
        bool aSecondLater = reads.TryTake(11.0);

        Assert.That(atOnce, Is.EqualTo(new[] { true, true, false }));
        Assert.That(aSecondLater, Is.True);
    }

    [Test]
    public void TheWords_NameWhatMoved_TheFee_AndTheRefusals()
    {
        Assert.That(
            new[]
            {
                StorageMessages.Describe(new StorageDelta(Gel, 10, 20), null),
                StorageMessages.Describe(new StorageDelta(Staff, -1, 20), null),
                StorageMessages.Describe(new StorageDelta(Staff, 1, 0), null),
                StorageMessages.Fee(20),
                StorageMessages.Fee(0)
            },
            Is.EqualTo(new[]
            {
                $"Deposited {Gel.Value} x 10 for 20 coins.",
                $"Withdrew {Staff.Value}.",
                $"Deposited {Staff.Value}.",
                "Each deposit costs 20 coins",
                "Deposits are free"
            }));
        Assert.That(
            new[]
            {
                StorageMessages.DescribeRefusal(StorageCommand.Deposit, CommandRejectionReason.NotEnoughCoins),
                StorageMessages.DescribeRefusal(StorageCommand.Deposit, CommandRejectionReason.InventoryFull),
                StorageMessages.DescribeRefusal(StorageCommand.Withdraw, CommandRejectionReason.InvalidTarget),
                StorageMessages.DescribeRefusal(StorageCommand.Open, CommandRejectionReason.ServiceUnavailable),
                StorageMessages.DescribeRefusal(StorageCommand.Withdraw, CommandRejectionReason.ItemActionInFlight)
            },
            Is.EqualTo(new[]
            {
                "You cannot pay the Storekeeper's fee.",
                "Storage is full.",
                "That is no longer in storage.",
                "Storage cannot be read right now.",
                RejectionMessages.Describe(CommandRejectionReason.ItemActionInFlight)
            }));
    }
}
}
