using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     An account's storage in the store (Persistence §5; owner decisions 4, 12, and 13 of the pre-Milestone-14
///     review): a deposit moves a bag row into storage for the fee, and a withdrawal by any character of the account
///     takes it out, each in one transaction with one ledger row, a stack merging, and a row keeping its refine level and
///     instance data; a repeat and the lookup answer from the ledger.
/// </summary>
[TestFixture]
public sealed class StorageStoreTests
{
    private const string Gel = "item.material.slime_gel";
    private const string Sword = "item.weapon.iron_sword";
    private const int GelStack = 999;
    private const int Fee = 20;
    private const int StorageRows = 300;
    private const int BagRows = 100;

    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private PostgresFixture m_database = null!;
    private PostgresGameStore m_store = null!;
    private Sql m_sql = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
        m_sql = new Sql(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    // An account with two characters, the first holding the coins.
    private (long Account, long First, long Second) NewAccount(long coins)
    {
        long account = m_sql.InsertAccount();
        long first = m_sql.InsertCharacter(account, Sql.UniqueName("Dep"));
        long second = m_sql.InsertCharacter(account, Sql.UniqueName("Wit"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {first}");
        return (account, first, second);
    }

    private long Give(long character, string item, int quantity, int refine = 0, string? instance = null)
    {
        string data = instance == null ? "NULL" : $"'{instance}'::jsonb";
        return m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, "
            + $"instance_data_json, version) VALUES ({character}, '{item}', {quantity}, {refine}, {data}, 0) "
            + "RETURNING id");
    }

    private StorageResult Deposit(long character, long row, int quantity, int stack, Guid? operation = null)
    {
        return m_store.CommitStorageDepositAsync(
                new StorageDepositCommit(
                    operation ?? Guid.NewGuid(),
                    character,
                    row,
                    quantity,
                    Fee,
                    stack,
                    StorageRows,
                    Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    // Both items stack to the same limit here, so a test names one limit for whichever item the row holds.
    private StorageResult Withdraw(long character, long storageRow, int quantity, int stack)
    {
        return Withdraw(character, storageRow, quantity,
            new Dictionary<string, int> { [Gel] = stack, [Sword] = stack });
    }

    private StorageResult Withdraw(
        long character,
        long storageRow,
        int quantity,
        IReadOnlyDictionary<string, int> stackLimits)
    {
        return m_store.CommitStorageWithdrawAsync(
                new StorageWithdrawCommit(Guid.NewGuid(), character, storageRow, quantity, stackLimits, BagRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private StoredStorage Read(long account)
    {
        return m_store.ReadStorageAsync(new AccountId(account), CancellationToken.None).GetAwaiter().GetResult();
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || quantity_delta || ' ' || currency_delta, ', ' "
            + $"ORDER BY id), '') FROM economy_ledger WHERE actor_character_id = {character}");
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    [Test]
    public void ARefinedRowWithInstanceData_KeepsThemThroughADepositAndAWithdrawal()
    {
        (long _, long character, long other) = NewAccount(Fee);
        long sword = Give(character, Sword, 1, 7, "{\"note\":\"kept\"}");

        StorageResult stored = Deposit(character, sword, 1, 1);
        StorageResult back = Withdraw(other, stored.StorageRow!.Id, 1, 1);

        Assert.That(stored.StorageRow.RefineLevel, Is.EqualTo(7));
        Assert.That(
            m_sql.Text(
                "SELECT refine_level || ' ' || (instance_data_json ->> 'note') FROM inventory_items "
                + $"WHERE id = {back.Row!.Id}"),
            Is.EqualTo("7 kept"));
        Assert.That((back.Row.RefineLevel, back.StorageRow!.Quantity), Is.EqualTo((7, 0)));
    }

    [Test]
    public void Deposit_OfAWholeStack_TakesTheFee_AndRaisesBothRevisions()
    {
        (long account, long character, long _) = NewAccount(25);
        long gel = Give(character, Gel, 10);

        StorageResult result = Deposit(character, gel, 10, GelStack);

        Assert.That(
            (result.Status, result.Coins, result.InventoryRevision, result.StorageRevision),
            Is.EqualTo((InventoryStatus.Committed, 5L, 1u, 1u)));
        Assert.That((result.Row!.Id, result.Row.Quantity), Is.EqualTo((gel, 0)), "the bag's row emptied");
        Assert.That(
            (result.StorageRow!.ItemDefinitionId, result.StorageRow.Quantity),
            Is.EqualTo((Gel, 10)),
            "the storage's row");
        Assert.That(Ledger(character), Is.EqualTo("storage_deposit -10 -20"));
        Assert.That(
            m_sql.Scalar(
                $"SELECT (metadata_json ->> 'storageItem')::bigint FROM economy_ledger WHERE actor_character_id = {character}"),
            Is.EqualTo(result.StorageRow.Id),
            "the ledger names the storage's row");
        StoredStorage storage = Read(account);
        Assert.That(
            (storage.Revision, storage.Items.Single().ItemDefinitionId, storage.Items.Single().Quantity),
            Is.EqualTo((1u, Gel, 10)));
    }

    [Test]
    public void Deposits_MergeIntoTheStoredStack_AndAnotherCharacterOfTheAccountWithdrawsPart()
    {
        (long account, long depositor, long other) = NewAccount(100);
        long gel = Give(depositor, Gel, 10);
        Deposit(depositor, gel, 4, GelStack);
        StorageResult second = Deposit(depositor, gel, 3, GelStack);

        StorageResult withdrawn = Withdraw(other, second.StorageRow!.Id, 5, GelStack);

        Assert.That(second.StorageRow.Quantity, Is.EqualTo(7), "one stack per item");
        Assert.That(
            (withdrawn.Status, withdrawn.Row!.ItemDefinitionId, withdrawn.Row.Quantity, withdrawn.StorageRow!.Quantity),
            Is.EqualTo((InventoryStatus.Committed, Gel, 5, 2)));
        Assert.That(
            (withdrawn.Coins, withdrawn.InventoryRevision, withdrawn.StorageRevision),
            Is.EqualTo((0L, 1u, 3u)),
            "a withdrawal is free");
        Assert.That(Ledger(other), Is.EqualTo("storage_withdraw 5 0"));
        Assert.That((Coins(depositor), Read(account).Items.Single().Quantity), Is.EqualTo((60L, 2)));
    }

    [Test]
    public void Refusals_AndFullness_ChangeNothing()
    {
        (long account, long character, long _) = NewAccount(Fee - 1);
        long gel = Give(character, Gel, 5);
        long worn = Give(character, Sword, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", worn));
        (long otherAccount, long rich, long _) = NewAccount(10_000);
        long richGel = Give(rich, Gel, 5);
        long stored = Deposit(rich, richGel, 1, GelStack).StorageRow!.Id;
        m_sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {otherAccount}, '{Sword}', 1, 0, 0 FROM generate_series(1, {StorageRows - 1})");
        long sword = Give(rich, Sword, 1);
        long full = NewAccount(0).First;
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {full}, '{Sword}', 1, 0, 0 FROM generate_series(1, {BagRows})");

        StorageResult[] results =
        {
            Deposit(character, gel, 5, GelStack),
            Deposit(character, worn, 1, 1),
            Deposit(rich, gel, 1, GelStack),
            Deposit(rich, richGel, 5, GelStack),
            Deposit(rich, sword, 1, 1),
            Withdraw(character, stored, 1, GelStack),
            Withdraw(rich, stored, 2, GelStack),
            Withdraw(full, Read(otherAccount).Items.Last().Id, 1, 1)
        };

        Assert.That(
            results.Select(result => result.Status),
            Is.EqualTo(new[]
            {
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.InventoryFull,
                InventoryStatus.Refused,
                InventoryStatus.Refused,
                InventoryStatus.Refused
            }),
            "a fee short, a worn row, another's row, too many, storage full, another account's storage, too many, "
            + "and another account's row again");
        Assert.That((Ledger(character), Coins(character)), Is.EqualTo((string.Empty, (long)(Fee - 1))));
        Assert.That(Read(account).Items, Is.Empty);
        Assert.That(Read(otherAccount).Revision, Is.EqualTo(1u), "only the first deposit");
    }

    [Test]
    public void Repeats_OfOneDeposit_MoveOnce_TheLookupFindsIt_AndStorageCountsAtStartup()
    {
        (long account, long character, long _) = NewAccount(100);
        long gel = Give(character, Gel, 10);
        var operation = Guid.NewGuid();
        StorageResult? before = m_store.FindStorageOperationAsync(operation, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Deposit(character, gel, 6, GelStack, operation);
        StorageResult repeat = Deposit(character, gel, 6, GelStack, operation);
        StorageResult? found = m_store.FindStorageOperationAsync(operation, character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        m_sql.Execute(
            $"UPDATE storage_items SET item_definition_id = 'item.material.only_stored' WHERE account_id = {account}");

        Assert.That(before, Is.Null);
        Assert.That(
            (repeat.Status, repeat.Coins, repeat.Row!.Quantity, repeat.StorageRow!.Quantity),
            Is.EqualTo((InventoryStatus.Committed, 80L, 4, 6)));
        Assert.That((found!.StorageRevision, found.InventoryRevision), Is.EqualTo((1u, 1u)));
        Assert.That(Ledger(character), Is.EqualTo("storage_deposit -6 -20"));
        Assert.That(
            m_store.ListStoredDefinitionIdsAsync(CancellationToken.None).GetAwaiter().GetResult(),
            Has.Member("item.material.only_stored"));
    }

    [Test]
    public void Storage_OfAnAccountThatNeverDeposited_IsEmptyAtRevisionZero()
    {
        StoredStorage storage = Read(m_sql.InsertAccount());

        Assert.That((storage.Revision, storage.Items.Count), Is.EqualTo((0u, 0)));
    }

    [Test]
    public void Withdraw_OfAnItemTheContentLacks_IsRefused_AndMovesNothing()
    {
        (long account, long character, long other) = NewAccount(Fee);
        long sword = Give(character, Sword, 1);
        StorageResult stored = Deposit(character, sword, 1, 1);

        StorageResult refused = Withdraw(
            other,
            stored.StorageRow!.Id,
            1,
            new Dictionary<string, int> { [Gel] = GelStack });

        Assert.That(
            (refused.Status, refused.StorageRevision, Read(account).Items.Single().Id),
            Is.EqualTo((InventoryStatus.Refused, 1u, stored.StorageRow.Id)));
    }

    [Test]
    public void Withdrawal_IntoAFullBag_IsFull_AndChangesNothing()
    {
        (long account, long character, long other) = NewAccount(Fee);
        long gel = Give(character, Gel, 1);
        long stored = Deposit(character, gel, 1, GelStack).StorageRow!.Id;
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"SELECT {other}, '{Sword}', 1, 0, 0 FROM generate_series(1, {BagRows})");

        StorageResult full = Withdraw(other, stored, 1, GelStack);

        Assert.That(full.Status, Is.EqualTo(InventoryStatus.InventoryFull));
        Assert.That((Ledger(other), Read(account).Revision), Is.EqualTo((string.Empty, 1u)));
    }
}
}
