using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Equip and unequip commit like a pickup (Persistence §5): one transaction under the character's lock, a ledger
///     row with zero deltas, the revision up by one, a repeat answered from the ledger, and every changed row reported
///     with its slot. A swap is one operation; an item with a stack limit of 1 takes a row per unit.
/// </summary>
[TestFixture]
public sealed class EquipmentStoreTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Weapon = "Weapon";
    private const int MaxRows = 100;

    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

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

    private long NewCharacter()
    {
        return m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Gear"));
    }

    // A sword picked up: a stack limit of 1 gives it a row of its own.
    private long PickUpSword(long character)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(Guid.NewGuid(), character, Sword, 1, 1, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Rows.Single()
            .Id;
    }

    private InventoryResult Equip(long character, long row, Guid? operation = null, long reportRow = 0)
    {
        long[] report = reportRow == 0 ? Array.Empty<long>() : new[] { reportRow };
        return m_store.CommitEquipAsync(
                new EquipCommit(operation ?? Guid.NewGuid(), character, row, Weapon, report, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Unequip(long character, Guid? operation = null)
    {
        return m_store.CommitUnequipAsync(
                new UnequipCommit(operation ?? Guid.NewGuid(), character, Weapon, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private long WornInSlot(long character)
    {
        return m_sql.Scalar(
            $"SELECT coalesce(max(inventory_item_id), 0) FROM equipment WHERE character_id = {character} "
            + $"AND slot = '{Weapon}'");
    }

    private static (long, string?)[] Slots(InventoryResult result)
    {
        return result.Rows.Select(row => (row.Id, row.EquippedSlot)).ToArray();
    }

    [Test]
    public void Equip_IntoAFullSlot_SwapsInOneOperation_AndTheLookupReportsBothRows()
    {
        long character = NewCharacter();
        long first = PickUpSword(character);
        long second = PickUpSword(character);
        Equip(character, first);
        var operation = Guid.NewGuid();

        InventoryResult swap = Equip(character, second, operation, first);
        InventoryResult? found = m_store
            .FindOperationAsync(operation, character, new[] { first }, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(first, Is.Not.EqualTo(second), "a row per unit");
        Assert.That(Slots(swap), Is.EqualTo(new (long, string?)[] { (second, Weapon), (first, null) }));
        Assert.That(Slots(found!), Is.EqualTo(Slots(swap)));
        Assert.That(WornInSlot(character), Is.EqualTo(second));
    }

    [Test]
    public void Equip_IntoAnEmptySlot_WearsTheRow_WithOneLedgerRowAndOneRevision()
    {
        long character = NewCharacter();
        long sword = PickUpSword(character);
        long revision = Revision(character);
        var operation = Guid.NewGuid();

        InventoryResult result = Equip(character, sword, operation);

        Assert.That(result.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Slots(result), Is.EqualTo(new (long, string?)[] { (sword, Weapon) }));
        Assert.That(result.InventoryRevision, Is.EqualTo((uint)(revision + 1)));
        Assert.That(WornInSlot(character), Is.EqualTo(sword));
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE operation_id = '{operation}' AND operation_type = 'equip' "
                + $"AND item_instance_id = {sword} AND quantity_delta = 0 AND currency_delta = 0"),
            Is.EqualTo(1));
    }

    [Test]
    public void Equips_OfTheSameRowAtOnce_CommitOnceEach_AndLeaveItWorn()
    {
        long character = NewCharacter();
        long sword = PickUpSword(character);
        long before = Revision(character);

        Task<InventoryResult>[] running = Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => Equip(character, sword)))
            .ToArray();
        Task.WaitAll(running);

        InventoryStatus[] statuses = running.Select(task => task.Result.Status).OrderBy(status => status).ToArray();
        Assert.That(statuses, Is.EqualTo(new[] { InventoryStatus.Committed, InventoryStatus.Refused }));
        Assert.That(Revision(character), Is.EqualTo(before + 1));
        Assert.That(WornInSlot(character), Is.EqualTo(sword));
    }

    [Test]
    public void Load_OfACharacterWearingAnItem_ReportsItsSlot()
    {
        long account = m_sql.InsertAccount();
        long character = m_sql.InsertCharacter(account, Sql.UniqueName("Worn"));
        long sword = PickUpSword(character);
        long spare = PickUpSword(character);
        Equip(character, sword);

        StoredCharacter stored = m_store.LoadCharacterAsync(new AccountId(account), character, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!;

        Assert.That(
            stored.Items.Select(item => (item.Id, item.EquippedSlot)),
            Is.EqualTo(new (long, string?)[] { (sword, Weapon), (spare, null) }));
    }

    [Test]
    public void Pickups_OfAStackLimitOneItem_TakeARowEach_UpToTheRowLimit()
    {
        long character = NewCharacter();
        long first = PickUpSword(character);
        long second = PickUpSword(character);
        for (int index = 0; index < MaxRows - 3; index++)
        {
            m_sql.InsertItem(character, 1);
        }

        long last = PickUpSword(character);
        InventoryResult full = m_store.CommitPickupAsync(
                new PickupCommit(Guid.NewGuid(), character, Sword, 1, 1, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(new[] { first, second, last }, Is.Unique);
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM inventory_items WHERE character_id = {character} "
                + $"AND item_definition_id = '{Sword}' AND quantity = 1"),
            Is.EqualTo(3));
        Assert.That(full.Status, Is.EqualTo(InventoryStatus.InventoryFull));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM inventory_items WHERE character_id = {character}"),
            Is.EqualTo(MaxRows));
    }

    [Test]
    public void Refusals_ChangeNothing_ForAnotherCharactersRow_ARowAlreadyWorn_OrAnEmptySlot()
    {
        long character = NewCharacter();
        long other = NewCharacter();
        long theirs = PickUpSword(other);
        long mine = PickUpSword(character);
        Equip(character, mine);
        long before = Revision(character);

        InventoryResult stolen = Equip(character, theirs);
        InventoryResult twice = Equip(character, mine);
        Unequip(character);
        long afterUnequip = Revision(character);
        InventoryResult empty = Unequip(character);

        Assert.That(
            new[] { stolen.Status, twice.Status, empty.Status },
            Is.All.EqualTo(InventoryStatus.Refused));
        Assert.That(before + 1, Is.EqualTo(afterUnequip), "only the unequip changed anything");
        Assert.That(Revision(character), Is.EqualTo(afterUnequip));
        Assert.That(WornInSlot(other), Is.Zero);
    }

    [Test]
    public void Repeats_OfOneOperation_ChangeTheInventoryOnce()
    {
        long character = NewCharacter();
        long sword = PickUpSword(character);
        var equip = Guid.NewGuid();
        var unequip = Guid.NewGuid();
        long before = Revision(character);

        InventoryResult first = Equip(character, sword, equip);
        InventoryResult again = Equip(character, sword, equip);
        InventoryResult off = Unequip(character, unequip);
        InventoryResult offAgain = Unequip(character, unequip);

        Assert.That(
            new[] { first.Status, again.Status, off.Status, offAgain.Status },
            Is.All.EqualTo(InventoryStatus.Committed));
        Assert.That(Revision(character), Is.EqualTo(before + 2));
        Assert.That(Slots(offAgain), Is.EqualTo(new (long, string?)[] { (sword, null) }));
        Assert.That(WornInSlot(character), Is.Zero);
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
                + "AND operation_type IN ('equip', 'unequip')"),
            Is.EqualTo(2));
    }
}
}
