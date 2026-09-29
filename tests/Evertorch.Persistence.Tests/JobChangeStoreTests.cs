using System;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     The job change in the store (Persistence §5, §6): one transaction writes the new job at job level 1 with job
///     experience 0 and takes a worn weapon off with an <c>unequip</c> ledger row; a repeat changes nothing, a lost
///     answer is settled from the stored job, and neither a checkpoint nor a quest reward taken in the old job can
///     write its job pair over the new one.
/// </summary>
[TestFixture]
public sealed class JobChangeStoreTests
{
    private const string Adventurer = "job.adventurer";
    private const string Vanguard = "job.vanguard";
    private const string Staff = "item.weapon.training_staff";

    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

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

    // An Adventurer at job level 10 with 470 job experience, wearing the staff when asked; returns it and the staff's
    // row, or 0.
    private (long Character, long Staff) NewCharacter(bool isWearingTheStaff)
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Job"));
        m_sql.Execute($"UPDATE characters SET job_level = 10, job_exp = 470 WHERE id = {character}");
        if (!isWearingTheStaff)
        {
            return (character, 0);
        }

        long staff = m_sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, '{Staff}', 1, 0, 0) RETURNING id");
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", staff));
        return (character, staff);
    }

    private InventoryResult Change(long character, Guid operation, string from = Adventurer, string? slot = "Weapon")
    {
        return m_store
            .CommitJobChangeAsync(
                new JobChangeCommit(operation, character, from, Vanguard, slot, Now),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private string Job(long character)
    {
        return m_sql.Text(
            $"SELECT job_definition_id || ' ' || job_level || ' ' || job_exp FROM characters WHERE id = {character}");
    }

    private string Worn(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(slot || ' ' || inventory_item_id, ', '), '') FROM equipment "
            + $"WHERE character_id = {character}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || operation_id || ' ' || item_instance_id, ', ' "
            + $"ORDER BY id), '') FROM economy_ledger WHERE actor_character_id = {character}");
    }

    private long Revision(long character)
    {
        return m_sql.Scalar($"SELECT inventory_revision FROM characters WHERE id = {character}");
    }

    private void Checkpoint(long character, string job, int jobLevel, long jobExperience, bool isCommitInFlight = false)
    {
        m_store.SaveCheckpointAsync(
                new CharacterCheckpoint(
                    character,
                    job,
                    new MapDefinitionId("map.training_ground"),
                    new WorldPosition(1f, 0f, 2f),
                    40,
                    15,
                    3,
                    70,
                    Now,
                    isCommitInFlight: isCommitInFlight,
                    jobLevel: jobLevel,
                    jobExperience: jobExperience),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [Test]
    public void Change_FromAJobTheCharacterDoesNotHave_IsRefusedAndChangesNothing()
    {
        (long character, _) = NewCharacter(true);
        string worn = Worn(character);

        InventoryResult refused = Change(character, Guid.NewGuid(), "job.arcanist");

        Assert.That(refused.Status, Is.EqualTo(InventoryStatus.Refused));
        Assert.That(Job(character), Is.EqualTo($"{Adventurer} 10 470"));
        Assert.That((Worn(character), Ledger(character)), Is.EqualTo((worn, string.Empty)));
    }

    // A replay after the change (the server's retry, or a second request that slipped past its checks) finds the job
    // already the new one and answers from the ledger, changing nothing: the job level earned since stays.
    [Test]
    public void Change_Repeated_ChangesOnceAndAnswersFromTheLedger()
    {
        (long character, long staff) = NewCharacter(true);
        var operation = Guid.NewGuid();
        InventoryResult first = Change(character, operation);
        m_sql.Execute($"UPDATE characters SET job_level = 2, job_exp = 40 WHERE id = {character}");

        InventoryResult again = Change(character, operation);

        Assert.That(again.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(again.InventoryRevision, Is.EqualTo(first.InventoryRevision));
        Assert.That(again.Rows.Select(row => (row.Id, row.EquippedSlot)), Is.EqualTo(new[] { (staff, (string?)null) }));
        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 2 40"), "nothing written again");
        Assert.That(Ledger(character).Split(',').Length, Is.EqualTo(1));
    }

    [Test]
    public void Change_WhileTheStaffIsWorn_WritesTheJobAndTakesTheStaffOffWithOneLedgerRow()
    {
        (long character, long staff) = NewCharacter(true);
        long revision = Revision(character);
        var operation = Guid.NewGuid();

        InventoryResult changed = Change(character, operation);

        Assert.That(changed.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(
            changed.Rows.Select(row => (row.Id, row.ItemDefinitionId, row.Quantity, row.EquippedSlot)),
            Is.EqualTo(new[] { (staff, Staff, 1, (string?)null) }),
            "the staff, still held, out of its slot");
        Assert.That(changed.InventoryRevision, Is.EqualTo(revision + 1));
        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 1 0"));
        Assert.That(Worn(character), Is.Empty);
        Assert.That(Ledger(character), Is.EqualTo($"unequip {operation} {staff}"));
    }

    [Test]
    public void Change_WithNothingWorn_WritesTheJobWithoutALedgerRowOrANewRevision()
    {
        (long character, _) = NewCharacter(false);
        long revision = Revision(character);

        InventoryResult changed = Change(character, Guid.NewGuid());

        Assert.That((changed.Status, changed.Rows.Count), Is.EqualTo((InventoryStatus.Committed, 0)));
        Assert.That((changed.InventoryRevision, Revision(character)), Is.EqualTo((revision, revision)));
        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 1 0"));
        Assert.That(Ledger(character), Is.Empty, "the job is not value");
    }

    [Test]
    public void Change_WithTheSlotAlreadyEmpty_StillChangesTheJob()
    {
        (long character, _) = NewCharacter(false);

        InventoryResult changed = Change(character, Guid.NewGuid());

        Assert.That(changed.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 1 0"));
    }

    [Test]
    public void Checkpoint_InTheNewJob_RaisesItsJobPair()
    {
        (long character, _) = NewCharacter(false);
        Change(character, Guid.NewGuid());

        Checkpoint(character, Vanguard, 2, 30);

        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 2 30"));
    }

    // A checkpoint taken before the change, and written after it, carries the old job at job level 10: it must not
    // store a first job at its base job's level, nor undo the change (Persistence §6).
    [Test]
    public void Checkpoint_TakenInTheOldJob_NeverWritesItsJobPairOverTheChange()
    {
        (long character, _) = NewCharacter(false);
        Change(character, Guid.NewGuid());

        Checkpoint(character, Adventurer, 10, 470);

        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 1 0"));
        Assert.That(
            m_sql.Text($"SELECT base_level || ' ' || map_definition_id FROM characters WHERE id = {character}"),
            Is.EqualTo("3 map.training_ground"),
            "the rest of the checkpoint is written");
    }

    // While a change is in flight the checkpoint leaves the job pair, the statistics, and the skills alone, as it does
    // for a turn-in.
    [Test]
    public void Checkpoint_WhileACommitIsInFlight_LeavesTheJobPairAlone()
    {
        (long character, _) = NewCharacter(false);

        Checkpoint(character, Adventurer, 10, 900, true);

        Assert.That(Job(character), Is.EqualTo($"{Adventurer} 10 470"));
    }

    // A lost answer is settled from the stored job (Persistence §5).
    [Test]
    public void FindJobChange_ForTheStoredJob_AnswersTheChangeAndOtherwiseNothing()
    {
        (long character, long staff) = NewCharacter(true);
        var operation = Guid.NewGuid();
        InventoryResult? before = m_store.FindJobChangeAsync(operation, character, Vanguard, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Change(character, operation);

        InventoryResult? after = m_store.FindJobChangeAsync(operation, character, Vanguard, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(before, Is.Null, "not changed yet");
        Assert.That(after!.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(after.Rows.Select(row => (row.Id, row.EquippedSlot)), Is.EqualTo(new[] { (staff, (string?)null) }));
    }

    [Test]
    public void QuestReward_CarriedInTheOldJob_NeverWritesItsJobPairOverTheChange()
    {
        (long character, _) = NewCharacter(false);
        Change(character, Guid.NewGuid());

        InventoryResult paid = m_store.CommitQuestRewardAsync(
                new QuestRewardCommit(
                    Guid.NewGuid(),
                    character,
                    Adventurer,
                    "quest.crawler_hunt",
                    5,
                    5,
                    100,
                    3,
                    70,
                    Now,
                    10,
                    620),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(paid.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Job(character), Is.EqualTo($"{Vanguard} 1 0"));
    }
}
}
