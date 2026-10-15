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
///     with its slot. A swap is one operation; an item with a stack limit of 1 takes a row per unit. A row worn in any
///     position is refused before anything is written, and the ledger names each row's position (Milestone 20).
/// </summary>
[TestFixture]
public sealed class EquipmentStoreTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Charm = "item.accessory.traveler_charm";
    private const string Weapon = "Weapon";
    private const string FirstAccessory = "Accessory1";
    private const string SecondAccessory = "Accessory2";
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
        return PickUp(character, Sword);
    }

    private long PickUp(long character, string item)
    {
        return m_store.CommitPickupAsync(
                new PickupCommit(Guid.NewGuid(), character, item, 1, 1, MaxRows, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Rows.Single()
            .Id;
    }

    private InventoryResult Equip(
        long character,
        long row,
        Guid? operation = null,
        long reportRow = 0,
        string slot = Weapon)
    {
        ReportedRow[] report =
            reportRow == 0 ? Array.Empty<ReportedRow>() : new[] { new ReportedRow(reportRow, Sword) };
        return m_store.CommitEquipAsync(
                new EquipCommit(operation ?? Guid.NewGuid(), character, row, slot, report, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult Unequip(long character, Guid? operation = null, string slot = Weapon)
    {
        return m_store.CommitUnequipAsync(
                new UnequipCommit(operation ?? Guid.NewGuid(), character, slot, Now),
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

    private string Worn(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(slot || ' ' || inventory_item_id, ', ' ORDER BY slot), '') FROM equipment "
            + $"WHERE character_id = {character}");
    }

    private string LedgerSlot(Guid operation)
    {
        return m_sql.Text($"SELECT metadata_json ->> 'slot' FROM economy_ledger WHERE operation_id = '{operation}'");
    }

    [Test]
    public void Equip_AndUnequip_InAnAccessoryPosition_NameItInTheLedger_AndARepeatAnswersWithTheSlot()
    {
        long character = NewCharacter();
        long charm = PickUp(character, Charm);
        var equip = Guid.NewGuid();
        var unequip = Guid.NewGuid();

        InventoryResult worn = Equip(character, charm, equip, slot: SecondAccessory);
        InventoryResult again = Equip(character, charm, equip, slot: SecondAccessory);
        Unequip(character, unequip, SecondAccessory);

        Assert.That(Slots(worn), Is.EqualTo(new (long, string?)[] { (charm, SecondAccessory) }));
        Assert.That(again.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Slots(again), Is.EqualTo(Slots(worn)), "the repeat answers with the slot");
        Assert.That(Worn(character), Is.Empty);
        Assert.That((LedgerSlot(equip), LedgerSlot(unequip)), Is.EqualTo((SecondAccessory, SecondAccessory)));
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
            .FindOperationAsync(operation, character, new[] { new ReportedRow(first, Sword) }, CancellationToken.None)
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

    // Moving a worn row to the other accessory position takes it off first (Persistence §5): the store refuses the move
    // under the lock before it writes, so it never meets the unique index, whether the destination is empty or held.
    [Test]
    public void Equip_OfARowWornInTheOtherAccessoryPosition_IsRefused_WhetherTheDestinationIsEmptyOrHeld()
    {
        long character = NewCharacter();
        long first = PickUp(character, Charm);
        long second = PickUp(character, Charm);
        Equip(character, first, slot: FirstAccessory);
        long before = Revision(character);

        InventoryResult toEmpty = Equip(character, first, slot: SecondAccessory);
        InventoryResult other = Equip(character, second, slot: SecondAccessory);
        InventoryResult toHeld = Equip(character, first, slot: SecondAccessory);
        InventoryResult toItsOwn = Equip(character, first, slot: FirstAccessory);

        Assert.That(
            new[] { toEmpty.Status, other.Status, toHeld.Status, toItsOwn.Status },
            Is.EqualTo(
                new[]
                {
                    InventoryStatus.Refused, InventoryStatus.Committed, InventoryStatus.Refused,
                    InventoryStatus.Refused
                }));
        Assert.That(Revision(character), Is.EqualTo(before + 1), "only the second charm's equip");
        Assert.That(Worn(character), Is.EqualTo($"{FirstAccessory} {first}, {SecondAccessory} {second}"));
        Assert.That(
            m_sql.Scalar(
                $"SELECT count(*) FROM economy_ledger WHERE actor_character_id = {character} "
                + "AND operation_type = 'equip'"),
            Is.EqualTo(2));
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
