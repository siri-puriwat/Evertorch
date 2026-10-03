using System;
using System.Threading;
using Evertorch.Game;
using Evertorch.Persistence;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The in-memory store keeps PostgreSQL's storage rules (Persistence §5; <c>StorageStoreTests</c>): one storage per
///     account, the fee on a deposit, a refusal or fullness changing nothing, and its switch losing a committed
///     answer, which the lookup then finds.
/// </summary>
[TestFixture]
public sealed class InMemoryStorageStoreTests
{
    private const string Gel = "item.material.slime_gel";

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryGameStore m_store = new();

    private long NewCharacter(AccountId account, long coins)
    {
        var character = new NewCharacter(
            $"Mem{Guid.NewGuid():N}".Substring(0, 12),
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 2f),
            Now);
        long id = m_store.CreateCharacterAsync(account, character, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .CharacterId;
        m_store.Edit(id, coins: coins);
        return id;
    }

    private AccountId NewAccount()
    {
        return m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
    }

    private StorageResult Deposit(long character, long row, int quantity, Guid operation)
    {
        return m_store.CommitStorageDepositAsync(
                new StorageDepositCommit(operation, character, row, quantity, 20, 999, 300, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Test]
    public void ADeposit_TheCoinsCannotPayFor_IsRefused_AndChangesNothing()
    {
        long character = NewCharacter(NewAccount(), 19);
        m_store.GiveItems(character, Gel, 1, 10, 0);
        long gel = m_store.Stored(character).Items[0].Id;

        StorageResult refused = Deposit(character, gel, 1, Guid.NewGuid());

        Assert.That((refused.Status, refused.Coins, refused.InventoryRevision),
            Is.EqualTo((InventoryStatus.Refused, 19L, 0u)));
        Assert.That(m_store.Stored(character).Items[0].Quantity, Is.EqualTo(10));
    }

    [Test]
    public void ADeposit_WhoseAnswerIsLost_IsFound_AndAnotherCharacterOfTheAccountWithdrawsIt()
    {
        AccountId account = NewAccount();
        long depositor = NewCharacter(account, 25);
        long other = NewCharacter(account, 0);
        m_store.GiveItems(depositor, Gel, 1, 10, 0);
        long gel = m_store.Stored(depositor).Items[0].Id;
        var operation = Guid.NewGuid();
        m_store.AmbiguousStorageFailures = 1;

        Action lost = () => Deposit(depositor, gel, 10, operation);

        Assert.That(lost, Throws.InstanceOf<StoreUnavailableException>());
        StorageResult? found = m_store.FindStorageOperationAsync(operation, depositor, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(
            (found!.Status, found.Coins, found.Row!.Quantity, found.StorageRow!.Quantity, found.StorageRevision),
            Is.EqualTo((InventoryStatus.Committed, 5L, 0, 10, 1u)));
        StorageResult back = m_store.CommitStorageWithdrawAsync(
                new StorageWithdrawCommit(Guid.NewGuid(), other, found.StorageRow.Id, 4, 999, 100, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(
            (back.Status, back.Row!.Quantity, back.StorageRow!.Quantity, back.Coins),
            Is.EqualTo((InventoryStatus.Committed, 4, 6, 0L)));
        StoredStorage storage = m_store.ReadStorageAsync(account, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That((storage.Revision, storage.Items[0].Quantity), Is.EqualTo((2u, 6)));
    }
}
}
