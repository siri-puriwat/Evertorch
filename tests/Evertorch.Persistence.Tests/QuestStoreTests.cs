using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Quests in the store (Persistence §5, §6; the vectors of the quests research note §4): checkpoints add an accepted
///     quest and raise its progress, never lower it, and never reopen a completed one; a turn-in completes the quest and
///     pays its reward once, in one transaction with one ledger row, and never lowers the level and experience.
/// </summary>
[TestFixture]
public sealed class QuestStoreTests
{
    private const string Hunt = "quest.crawler_hunt";
    private const int Count = 5;
    private const int Reward = 100;
    private const long Cap = 1_000_000_000;

    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

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

    private long NewCharacter(long coins = 0)
    {
        long character = m_sql.InsertCharacter(m_sql.InsertAccount(), Sql.UniqueName("Quest"));
        m_sql.Execute($"UPDATE characters SET currency = {coins} WHERE id = {character}");
        return character;
    }

    private void Checkpoint(
        long character,
        int level,
        long experience,
        bool isRewardInFlight = false,
        params StoredQuest[] quests)
    {
        m_store.SaveCheckpointAsync(
                new CharacterCheckpoint(
                    character,
                    new MapDefinitionId("map.training_ground"),
                    new WorldPosition(1f, 0f, 2f),
                    40,
                    15,
                    level,
                    experience,
                    Now,
                    quests,
                    isRewardInFlight),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private InventoryResult TurnIn(
        long character,
        int progress = Count,
        int level = 3,
        long experience = 70,
        Guid? operation = null)
    {
        return TurnInAsync(character, progress, level, experience, operation ?? Guid.NewGuid())
            .GetAwaiter()
            .GetResult();
    }

    private Task<InventoryResult> TurnInAsync(long character, int progress, int level, long experience, Guid operation)
    {
        return m_store.CommitQuestRewardAsync(
            new QuestRewardCommit(operation, character, Hunt, progress, Count, Reward, level, experience, Now),
            CancellationToken.None);
    }

    private string Quest(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(state || ' ' || progress, ', '), '') FROM character_quests "
            + $"WHERE character_id = {character} AND quest_definition_id = '{Hunt}'");
    }

    private string Progression(long character)
    {
        return m_sql.Text($"SELECT base_level || ' ' || base_exp FROM characters WHERE id = {character}");
    }

    private long Coins(long character)
    {
        return m_sql.Scalar($"SELECT currency FROM characters WHERE id = {character}");
    }

    private string Ledger(long character)
    {
        return m_sql.Text(
            "SELECT coalesce(string_agg(operation_type || ' ' || quantity_delta || ' ' || currency_delta || ' ' "
            + "|| coalesce(metadata_json->>'quest', ''), ', ' ORDER BY id), '') FROM economy_ledger "
            + $"WHERE actor_character_id = {character}");
    }

    [TestCase("completed", Count, 0L, "completed already")]
    [TestCase("active", Count - 1, 0L, "short of the count")]
    [TestCase("active", Count, Cap - 50, "past the coin cap")]
    public void TurnIn_ThatFailsACheck_IsRefused_AndChangesNothing(
        string state,
        int progress,
        long coins,
        string reason)
    {
        long character = NewCharacter(coins);
        m_sql.Execute(Sql.QuestInsert(character, Hunt, state, state == "completed" ? Count : 0));

        InventoryResult refused = TurnIn(character, progress);

        Assert.That((refused.Status, refused.Coins), Is.EqualTo((InventoryStatus.Refused, coins)), reason);
        Assert.That(Coins(character), Is.EqualTo(coins), reason);
        Assert.That(Progression(character), Is.EqualTo("1 0"), reason);
        Assert.That(Ledger(character), Is.Empty, reason);
    }

    [Test]
    public void Checkpoint_AddsAnAcceptedQuest_AndOnlyEverRaisesItsProgress()
    {
        long character = NewCharacter();

        Checkpoint(character, 1, 0, false, new StoredQuest(Hunt, false, 0));
        string accepted = Quest(character);
        Checkpoint(character, 1, 0, false, new StoredQuest(Hunt, false, 3));
        string advanced = Quest(character);
        Checkpoint(character, 1, 0, false, new StoredQuest(Hunt, false, 1));

        Assert.That((accepted, advanced), Is.EqualTo(("active 0", "active 3")));
        Assert.That(Quest(character), Is.EqualTo("active 3"), "never lowered");
    }

    // Only the turn-in writes a completed quest: a checkpoint from memory that still held it active, even with more
    // progress than the row, leaves the row as it is.
    [Test]
    public void Checkpoint_NeverReopensACompletedQuest()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, Hunt, "completed", Count));

        Checkpoint(character, 1, 0, false, new StoredQuest(Hunt, false, Count + 1));

        Assert.That(Quest(character), Is.EqualTo("completed 5"));
    }

    // The vector "A later checkpoint": a checkpoint taken while the turn-in was in flight may carry more experience than
    // the turn-in's pair and still lack the reward, so it leaves level and experience to the turn-in, which makes the
    // reward durable at its commit; the next checkpoint writes what memory holds.
    [Test]
    public void Checkpoint_WhileATurnInIsInFlight_LeavesTheLevelAndExperienceToTheReward()
    {
        long character = NewCharacter();

        Checkpoint(character, 5, 0, true, new StoredQuest(Hunt, false, Count));
        string beforeTheCommit = Progression(character);
        string position = m_sql.Text($"SELECT position_x || ' ' || hp FROM characters WHERE id = {character}");
        TurnIn(character, level: 3, experience: 70);
        string atTheCommit = Progression(character);
        Checkpoint(character, 4, 15);

        Assert.That(beforeTheCommit, Is.EqualTo("1 0"), "left alone while the turn-in was in flight");
        Assert.That(position, Is.EqualTo("1 40"), "the rest of the checkpoint is written");
        Assert.That(atTheCommit, Is.EqualTo("3 70"), "the reward's pair is durable at the commit");
        Assert.That(Progression(character), Is.EqualTo("4 15"));
    }

    [Test]
    public void Load_ReadsTheQuests_OrderedByQuestId()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, "quest.b_later", "active", 2));
        m_sql.Execute(Sql.QuestInsert(character, "quest.a_first", "completed", 5));
        long account = m_sql.Scalar($"SELECT account_id FROM characters WHERE id = {character}");

        StoredCharacter? loaded = m_store
            .LoadCharacterAsync(new AccountId(account), character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(
            loaded!.Quests.Select(quest => (quest.QuestDefinitionId, quest.IsCompleted, quest.Progress)),
            Is.EqualTo(new[] { ("quest.a_first", true, 5), ("quest.b_later", false, 2) }));
    }

    [Test]
    public void Lookup_OfATurnIn_FindsItsCoins_AndNoRow()
    {
        long character = NewCharacter();
        var operation = Guid.NewGuid();
        TurnIn(character, operation: operation);

        InventoryResult? found = m_store
            .FindOperationAsync(operation, character, Array.Empty<long>(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((found!.Status, found.Coins, found.Rows.Count), Is.EqualTo((InventoryStatus.Committed, 100L, 0)));
    }

    [Test]
    public void StoredDefinitionIds_IncludeTheQuests()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, "quest.retired", "active", 1));

        Assert.That(
            m_store.ListStoredDefinitionIdsAsync(CancellationToken.None).GetAwaiter().GetResult(),
            Does.Contain("quest.retired"));
    }

    [Test]
    public void TurnIn_CompletesTheQuest_PaysTheRewardOnce_AndRecordsIt()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, Hunt, "active", Count));
        var operation = Guid.NewGuid();

        InventoryResult paid = TurnIn(character, operation: operation);
        InventoryResult repeat = TurnIn(character, operation: operation);

        Assert.That(
            (paid.Status, paid.Coins, paid.InventoryRevision, paid.Rows.Count),
            Is.EqualTo((InventoryStatus.Committed, 100L, 1u, 0)));
        Assert.That((repeat.Status, repeat.Coins), Is.EqualTo((InventoryStatus.Committed, 100L)), "found, not paid");
        Assert.That(
            (Quest(character), Progression(character), Coins(character)),
            Is.EqualTo(("completed 5", "3 70", 100L)));
        Assert.That(Ledger(character), Is.EqualTo($"quest_reward 0 100 {Hunt}"));
    }

    [Test]
    public void TurnIn_NeverLowersTheLevelAndExperience_ButStillPays()
    {
        long character = NewCharacter();
        m_sql.Execute($"UPDATE characters SET base_level = 4, base_exp = 10 WHERE id = {character}");

        InventoryResult paid = TurnIn(character, level: 3, experience: 70);

        Assert.That((paid.Status, Coins(character)), Is.EqualTo((InventoryStatus.Committed, 100L)));
        Assert.That(Progression(character), Is.EqualTo("4 10"));
    }

    [Test]
    public void TurnIn_WhenNoCheckpointWroteTheAcceptance_AddsTheQuestCompleted()
    {
        long character = NewCharacter();

        InventoryResult paid = TurnIn(character);

        Assert.That(paid.Status, Is.EqualTo(InventoryStatus.Committed));
        Assert.That(Quest(character), Is.EqualTo("completed 5"));
    }

    [Test]
    public void TurnIns_AtOnce_UnderTwoOperations_PayOnce()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, Hunt, "active", Count));

        InventoryResult[] results = Task.WhenAll(
                TurnInAsync(character, Count, 3, 70, Guid.NewGuid()),
                TurnInAsync(character, Count, 3, 70, Guid.NewGuid()))
            .GetAwaiter()
            .GetResult();

        Assert.That(
            results.Select(result => result.Status),
            Is.EquivalentTo(new[] { InventoryStatus.Committed, InventoryStatus.Refused }));
        Assert.That(Coins(character), Is.EqualTo(100L));
        Assert.That(Ledger(character), Is.EqualTo($"quest_reward 0 100 {Hunt}"));
    }
}
}
