using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Loading a character with its inventory, and writing checkpoints over it (Persistence §6, §7, §8).
/// </summary>
[TestFixture]
public sealed class CharacterLoadTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

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

    private (AccountId Account, long Character) NewCharacter()
    {
        AccountId account = m_store.ProvisionAccountAsync($"dev:{Guid.NewGuid():N}", Now, CancellationToken.None)
            .GetAwaiter()
            .GetResult()!.Value;
        var character = new NewCharacter(
            Sql.UniqueName("Load"),
            new JobDefinitionId("job.adventurer"),
            new PrimaryStats(5, 6, 7, 8, 9, 10),
            71,
            23,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 2f),
            Now);
        CharacterCreation created = m_store.CreateCharacterAsync(account, character, 3, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return (account, created.CharacterId);
    }

    private StoredCharacter? Load(AccountId account, long character)
    {
        return m_store.LoadCharacterAsync(account, character, CancellationToken.None).GetAwaiter().GetResult();
    }

    private void Checkpoint(long character, WorldPosition position, int health)
    {
        Checkpoint(character, position, health, 23, 1, 0);
    }

    private void Checkpoint(long character, WorldPosition position, int health, int spirit, int level, long experience)
    {
        var checkpoint = new CharacterCheckpoint(
            character,
            new MapDefinitionId("map.training_ground"),
            position,
            health,
            spirit,
            level,
            experience,
            Now);
        m_store.SaveCheckpointAsync(checkpoint, CancellationToken.None).GetAwaiter().GetResult();
    }

    private void SaveBuild(
        long character,
        int jobLevel,
        long jobExperience,
        PrimaryStats? stats,
        IReadOnlyList<StoredSkill>? skills,
        bool isRewardInFlight = false)
    {
        var checkpoint = new CharacterCheckpoint(
            character,
            new MapDefinitionId("map.training_ground"),
            new WorldPosition(1f, 0f, 1f),
            50,
            23,
            1,
            0,
            Now,
            isRewardInFlight: isRewardInFlight,
            jobLevel: jobLevel,
            jobExperience: jobExperience,
            stats: stats,
            skills: skills);
        m_store.SaveCheckpointAsync(checkpoint, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static StoredSkill[] Skills(params (string Skill, int Level)[] skills)
    {
        return skills.Select(skill => new StoredSkill(skill.Skill, skill.Level)).ToArray();
    }

    private static (string, int)[] LevelsOf(StoredCharacter stored)
    {
        return stored.Skills.Select(skill => (skill.SkillDefinitionId, skill.Level)).ToArray();
    }

    // Stored job level and job experience, then a checkpoint's => what is stored afterwards (Persistence §6).
    [TestCase(3, 40, 2, 99, 3, 40)]
    [TestCase(3, 40, 3, 39, 3, 40)]
    [TestCase(3, 40, 3, 41, 3, 41)]
    [TestCase(3, 40, 4, 0, 4, 0)]
    public void SaveCheckpoint_NeverLowersTheJobLevelAndJobExperience(
        int storedLevel,
        long storedExperience,
        int level,
        long experience,
        int expectedLevel,
        long expectedExperience)
    {
        (AccountId account, long character) = NewCharacter();
        SaveBuild(character, storedLevel, storedExperience, null, null);

        SaveBuild(character, level, experience, null, null);
        StoredCharacter stored = Load(account, character)!;

        Assert.That((stored.JobLevel, stored.JobExperience), Is.EqualTo((expectedLevel, expectedExperience)));
    }

    // Stored level and experience, then a checkpoint's => what is stored afterwards.
    [TestCase(3, 40, 2, 99, 3, 40)]
    [TestCase(3, 40, 3, 39, 3, 40)]
    [TestCase(3, 40, 3, 40, 3, 40)]
    [TestCase(3, 40, 3, 41, 3, 41)]
    [TestCase(3, 40, 4, 0, 4, 0)]
    public void SaveCheckpoint_NeverLowersTheLevelAndExperience_AndWritesTheRest(
        int storedLevel,
        long storedExperience,
        int level,
        long experience,
        int expectedLevel,
        long expectedExperience)
    {
        (AccountId account, long character) = NewCharacter();
        Checkpoint(character, new WorldPosition(1f, 0f, 1f), 50, 23, storedLevel, storedExperience);

        Checkpoint(character, new WorldPosition(2f, 0f, 2f), 40, 11, level, experience);
        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.BaseLevel, Is.EqualTo(expectedLevel), "level");
        Assert.That(stored.Experience, Is.EqualTo(expectedExperience), "experience");
        Assert.That(stored.Position, Is.EqualTo(new WorldPosition(2f, 0f, 2f)), "position");
        Assert.That(stored.Health, Is.EqualTo(40), "HP");
        Assert.That(stored.Spirit, Is.EqualTo(11), "SP");
    }

    [Test]
    public void ListStoredDefinitionIds_IncludesJobsMapsItemsAndSkillsEvenUnknownOnes()
    {
        (AccountId _, long character) = NewCharacter();
        m_sql.Execute(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, 'item.material.retired', 1, 0, 0)");
        m_sql.Execute(Sql.SkillInsert(character, "skill.retired", 2));

        IReadOnlyList<string> ids = m_store.ListStoredDefinitionIdsAsync(CancellationToken.None).GetAwaiter()
            .GetResult();

        Assert.That(ids, Does.Contain("job.adventurer"));
        Assert.That(ids, Does.Contain("map.training_ground"));
        Assert.That(ids, Does.Contain("item.material.retired"));
        Assert.That(ids, Does.Contain("skill.retired"));
    }

    [Test]
    public void LoadCharacter_ForANewCharacter_ReturnsLevelOneWithNoExperienceAndItsSp()
    {
        (AccountId account, long character) = NewCharacter();

        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.BaseLevel, Is.EqualTo(1));
        Assert.That(stored.Experience, Is.Zero);
        Assert.That(stored.Spirit, Is.EqualTo(23));
        Assert.That((stored.JobLevel, stored.JobExperience), Is.EqualTo((1, 0L)), "job level 1");
        Assert.That(stored.Skills, Is.Empty, "nothing learned");
    }

    [Test]
    public void LoadCharacter_ForItsAccount_ReturnsWhatWasStoredWithTheInventory()
    {
        (AccountId account, long character) = NewCharacter();
        long first = m_sql.InsertItem(character, 3);
        long second = m_sql.InsertItem(character, 7);

        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.Id, Is.EqualTo(character));
        Assert.That(stored.Account, Is.EqualTo(account));
        Assert.That(stored.JobDefinitionId, Is.EqualTo("job.adventurer"));
        Assert.That(stored.BaseLevel, Is.EqualTo(1));
        Assert.That(stored.Stats, Is.EqualTo(new PrimaryStats(5, 6, 7, 8, 9, 10)));
        Assert.That(stored.Health, Is.EqualTo(71));
        Assert.That(stored.MapDefinitionId, Is.EqualTo("map.training_ground"));
        Assert.That(stored.Position, Is.EqualTo(new WorldPosition(1f, 0f, 2f)));
        Assert.That(stored.InventoryRevision, Is.Zero);
        Assert.That(stored.Items, Has.Count.EqualTo(2));
        Assert.That(stored.Items[0].Id, Is.EqualTo(first));
        Assert.That(stored.Items[1].Id, Is.EqualTo(second));
        Assert.That(stored.Items[1].Quantity, Is.EqualTo(7));
        Assert.That(stored.Items[1].ItemDefinitionId, Is.EqualTo("item.material.slime_gel"));
    }

    [Test]
    public void LoadCharacter_OfAnotherAccountOrMissing_ReturnsNull()
    {
        (AccountId _, long character) = NewCharacter();
        (AccountId other, long _) = NewCharacter();

        Assert.That(Load(other, character), Is.Null);
        Assert.That(Load(other, long.MaxValue), Is.Null);
    }

    [Test]
    public void SaveCheckpoint_IncrementsTheVersion()
    {
        (AccountId _, long character) = NewCharacter();

        Checkpoint(character, new WorldPosition(1f, 0f, 1f), 50);
        Checkpoint(character, new WorldPosition(2f, 0f, 1f), 50);

        Assert.That(m_sql.Scalar($"SELECT version FROM characters WHERE id = {character}"), Is.EqualTo(2));
    }

    [Test]
    public void SaveCheckpoint_OfADeadCharacter_StoresZeroHealth()
    {
        (AccountId account, long character) = NewCharacter();

        Checkpoint(character, new WorldPosition(1f, 0f, 1f), 0);

        Assert.That(Load(account, character)!.Health, Is.Zero);
    }

    [Test]
    public void SaveCheckpoint_ThenLoad_ReturnsTheCheckpointedLevelExperienceAndSp()
    {
        (AccountId account, long character) = NewCharacter();

        Checkpoint(character, new WorldPosition(1f, 0f, 1f), 50, 17, 3, 42);
        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.BaseLevel, Is.EqualTo(3));
        Assert.That(stored.Experience, Is.EqualTo(42));
        Assert.That(stored.Spirit, Is.EqualTo(17));
    }

    [Test]
    public void SaveCheckpoint_ThenLoad_ReturnsTheCheckpointedPositionAndHealth()
    {
        (AccountId account, long character) = NewCharacter();

        Checkpoint(character, new WorldPosition(3.25f, 0.5f, -4.75f), 12);
        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.Position, Is.EqualTo(new WorldPosition(3.25f, 0.5f, -4.75f)));
        Assert.That(stored.Health, Is.EqualTo(12));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM characters WHERE id = {character} AND last_played_at IS NOT NULL"),
            Is.EqualTo(1));
    }

    // While a quest reward is in flight the build stays as stored, with the job pair: points spent from a level the
    // reward's commit has not yet made could otherwise outrun what the store says was earned (Persistence §6).
    [Test]
    public void SaveCheckpoint_WhileARewardIsInFlight_LeavesTheJobPairStatisticsAndSkills()
    {
        (AccountId account, long character) = NewCharacter();
        SaveBuild(character, 2, 10, new PrimaryStats(6, 6, 7, 8, 9, 10), Skills(("skill.strike", 1)));

        SaveBuild(
            character,
            3,
            0,
            new PrimaryStats(9, 9, 9, 9, 9, 10),
            Skills(("skill.strike", 1), ("skill.focus", 1)),
            true);
        StoredCharacter stored = Load(account, character)!;

        Assert.That((stored.JobLevel, stored.JobExperience), Is.EqualTo((2, 10L)));
        Assert.That(stored.Stats, Is.EqualTo(new PrimaryStats(6, 6, 7, 8, 9, 10)));
        Assert.That(LevelsOf(stored), Is.EqualTo(new[] { ("skill.strike", 1) }));
    }

    [Test]
    public void SaveCheckpoint_WithoutAStatisticsOrSkills_LeavesTheStoredOnes()
    {
        (AccountId account, long character) = NewCharacter();
        SaveBuild(character, 6, 0, new PrimaryStats(9, 12, 7, 8, 11, 10), Skills(("skill.strike", 2)));

        SaveBuild(character, 6, 0, null, null);
        StoredCharacter stored = Load(account, character)!;

        Assert.That(stored.Stats, Is.EqualTo(new PrimaryStats(9, 12, 7, 8, 11, 10)));
        Assert.That(LevelsOf(stored), Is.EqualTo(new[] { ("skill.strike", 2) }));
    }

    [Test]
    public void SaveCheckpoint_WritesTheStatisticsAndSkillsWhole_SoALowerBuildIsStoredAsItIs()
    {
        (AccountId account, long character) = NewCharacter();
        SaveBuild(character, 6, 0, new PrimaryStats(9, 12, 7, 8, 11, 10),
            Skills(("skill.focus", 3), ("skill.strike", 2)));
        StoredCharacter raised = Load(account, character)!;

        SaveBuild(character, 6, 0, new PrimaryStats(5, 6, 7, 8, 9, 10), Skills(("skill.strike", 1)));
        StoredCharacter reset = Load(account, character)!;

        Assert.That(raised.Stats, Is.EqualTo(new PrimaryStats(9, 12, 7, 8, 11, 10)));
        Assert.That(LevelsOf(raised), Is.EqualTo(new[] { ("skill.focus", 3), ("skill.strike", 2) }));
        Assert.That(reset.Stats, Is.EqualTo(new PrimaryStats(5, 6, 7, 8, 9, 10)), "lowered, written whole");
        Assert.That(LevelsOf(reset), Is.EqualTo(new[] { ("skill.strike", 1) }), "focus forgotten, strike lowered");
    }
}
}
