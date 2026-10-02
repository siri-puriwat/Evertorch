using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     The schema's own guarantees (Persistence §4, §11), proven with plain SQL so that no store code can hide a
///     missing constraint.
/// </summary>
[TestFixture]
public sealed class SchemaTests
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string RestrictViolation = "23001";
    private const string ValueTooLong = "22001";
    private const string AddCharacterSkills = "20260928181417_AddCharacterSkills";
    private const string AddParties = "20261001131541_AddParties";

    private PostgresFixture m_database = null!;
    private Sql m_sql = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_sql = new Sql(m_database.ConnectionString);
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    [TestCase("Abc")]
    [TestCase("Bad Name")]
    [TestCase("Bad_Name")]
    [TestCase("Näme1")]
    [TestCase("")]
    public void Character_WithAnInvalidName_IsRejected(string name)
    {
        long account = m_sql.InsertAccount();

        AssertFailsWith(CheckViolation, Sql.CharacterInsert(account, name, name.ToLowerInvariant(), 0));
    }

    [TestCase("Abcd")]
    [TestCase("Abcdefghijklmnopqrstuvw")]
    public void Character_WithANameAtTheLengthBounds_IsStored(string baseName)
    {
        long account = m_sql.InsertAccount();
        string name = $"{baseName.Substring(0, baseName.Length - 3)}{Guid.NewGuid():N}".Substring(0, baseName.Length);

        long id = m_sql.InsertCharacter(account, name);

        Assert.That(id, Is.Positive);
    }

    [TestCase(-1L)]
    [TestCase(4294967296L)]
    public void Character_WithAnInventoryRevisionOutsideThirtyTwoBits_IsRejected(long revision)
    {
        long account = m_sql.InsertAccount();
        string name = Sql.UniqueName("Rev");

        AssertFailsWith(CheckViolation, Sql.CharacterInsert(account, name, name.ToLowerInvariant(), revision));
    }

    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(1_000_001)]
    public void InventoryItem_WithAQuantityOutsideItsBounds_IsRejected(int quantity)
    {
        long character = NewCharacter();

        AssertFailsWith(CheckViolation, Sql.ItemInsert(character, quantity));
    }

    [TestCase(-1L)]
    [TestCase(1_000_000_001L)]
    public void Character_WithCoinsOutsideTheirBounds_IsRejected(long coins)
    {
        long account = m_sql.InsertAccount();
        string name = Sql.UniqueName("Coi");

        AssertFailsWith(CheckViolation, Sql.CharacterInsert(account, name, name.ToLowerInvariant(), 0, coins));
    }

    [TestCase(0L)]
    [TestCase(1_000_000_000L)]
    public void Character_AtTheCoinBounds_IsStored(long coins)
    {
        long account = m_sql.InsertAccount();
        string name = Sql.UniqueName("Cap");

        long id = m_sql.Scalar(Sql.CharacterInsert(account, name, name.ToLowerInvariant(), 0, coins));

        Assert.That(m_sql.Scalar($"SELECT currency FROM characters WHERE id = {id}"), Is.EqualTo(coins));
    }

    [TestCase("failed", 0)]
    [TestCase("active", -1)]
    public void CharacterQuest_WithAnUnknownStateOrNegativeProgress_IsRejected(string state, int progress)
    {
        long character = NewCharacter();

        AssertFailsWith(CheckViolation, Sql.QuestInsert(character, "quest.crawler_hunt", state, progress));
    }

    [TestCase(1)]
    [TestCase(1_000_000)]
    public void InventoryItem_AtTheQuantityBounds_IsStored(int quantity)
    {
        long character = NewCharacter();

        long id = m_sql.InsertItem(character, quantity);

        Assert.That(id, Is.Positive);
    }

    private long NewCharacter()
    {
        long account = m_sql.InsertAccount();
        return m_sql.InsertCharacter(account, Sql.UniqueName("Chr"));
    }

    private long NewLedgerEntry()
    {
        long character = NewCharacter();
        return m_sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "pickup"));
    }

    private static string? SqlStateOf(Sql sql, string commandText)
    {
        try
        {
            sql.Execute(commandText);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    private void AssertFailsWith(string sqlState, string commandText)
    {
        PostgresException? failure = null;
        try
        {
            m_sql.Execute(commandText);
        }
        catch (PostgresException exception)
        {
            failure = exception;
        }

        Assert.That(failure, Is.Not.Null, $"expected SQLSTATE {sqlState} from: {commandText}");
        Assert.That(failure!.SqlState, Is.EqualTo(sqlState), failure.MessageText);
    }

    [TestCase("pickup")]
    [TestCase("equip")]
    [TestCase("unequip")]
    [TestCase("consume")]
    [TestCase("buy")]
    [TestCase("sell")]
    [TestCase("quest_reward")]
    [TestCase("boss_reward")]
    public void Ledger_WithAKnownOperationType_IsStored(string operationType)
    {
        long character = NewCharacter();

        long entry = m_sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, operationType));

        Assert.That(m_sql.Scalar($"SELECT count(*) FROM economy_ledger WHERE id = {entry}"), Is.EqualTo(1));
    }

    [TestCase("'1000$c2FsdA==$a2V5'", "NULL")]
    [TestCase("NULL", "'pbkdf2-sha256'")]
    public void Account_WithOnlyOneOfItsPasswordHashAndScheme_IsRejected(string hash, string scheme)
    {
        AssertFailsWith(
            CheckViolation,
            "INSERT INTO accounts (login_normalized, password_hash, password_scheme, status, created_at) "
            + $"VALUES ('user{Guid.NewGuid():N}', {hash}, {scheme}, 'active', now())");
    }

    [TestCase(0)]
    [TestCase(31)]
    [TestCase(33)]
    public void SessionToken_WithAHashNotThirtyTwoBytes_IsRejected(int length)
    {
        long account = m_sql.InsertAccount();

        AssertFailsWith(CheckViolation, Sql.SessionTokenInsert(account, length));
    }

    [TestCase("0 minutes")]
    [TestCase("-1 minute")]
    public void SessionToken_ExpiringNoLaterThanItWasIssued_IsRejected(string lifetime)
    {
        long account = m_sql.InsertAccount();

        AssertFailsWith(CheckViolation, Sql.SessionTokenInsert(account, lifetime: lifetime));
    }

    [Test]
    public void Account_WithAPasswordHashAndScheme_IsStored()
    {
        long id = m_sql.Scalar(
            "INSERT INTO accounts (login_normalized, password_hash, password_scheme, status, created_at) "
            + $"VALUES ('user{Guid.NewGuid():N}', '1000$c2FsdA==$a2V5', 'pbkdf2-sha256', 'active', now()) RETURNING id");

        Assert.That(id, Is.Positive);
    }

    [Test]
    public void Account_WithARepeatedLogin_IsRejected()
    {
        string login = $"dev:{Guid.NewGuid():N}";
        string insert =
            $"INSERT INTO accounts (login_normalized, status, created_at) VALUES ('{login}', 'active', now())";
        m_sql.Execute(insert);

        AssertFailsWith(UniqueViolation, insert);
    }

    [Test]
    public void Account_WithASessionToken_CannotBeDeleted()
    {
        long account = m_sql.InsertAccount();
        m_sql.Execute(Sql.SessionTokenInsert(account));

        AssertFailsWith(RestrictViolation, $"DELETE FROM accounts WHERE id = {account}");
    }

    // AddBossRewards runs down only while no ledger row is a boss's prize, since the narrower check cannot hold over
    // one, and the ledger never forgets a row (Persistence §11).
    [Test]
    public void AddBossRewards_Down_IsRefusedOverABossReward_AndOtherwiseNarrowsTheTypesUntilUp()
    {
        using var rewarded = PostgresFixture.Start();
        var rewardedSql = new Sql(rewarded.ConnectionString);
        rewardedSql.Scalar(
            Sql.LedgerInsert(Guid.NewGuid(), rewardedSql.InsertCharacter(rewardedSql.InsertAccount(), "Rewarded"),
                "boss_reward"));
        using var plain = PostgresFixture.Start();
        var plainSql = new Sql(plain.ConnectionString);
        long character = plainSql.InsertCharacter(plainSql.InsertAccount(), Sql.UniqueName("Dn"));

        Exception? refused = null;
        try
        {
            EvertorchDatabase.ApplyMigrationsAsync(rewarded.ConnectionString, AddParties, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (PostgresException exception)
        {
            refused = exception;
        }

        EvertorchDatabase.ApplyMigrationsAsync(plain.ConnectionString, AddParties, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        string? afterDown = SqlStateOf(plainSql, Sql.LedgerInsert(Guid.NewGuid(), character, "boss_reward"));
        EvertorchDatabase.ApplyMigrationsAsync(plain.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((refused as PostgresException)?.SqlState, Is.EqualTo(CheckViolation), "the prize's row holds");
        Assert.That(afterDown, Is.EqualTo(CheckViolation), "no prize without the type");
        Assert.That(SqlStateOf(plainSql, Sql.LedgerInsert(Guid.NewGuid(), character, "boss_reward")), Is.Null);
    }

    // AddParties runs down and up again (Persistence §11): no ledger row refers to a party, so the down migration
    // only drops the two tables and the leader's key.
    [Test]
    public void AddParties_Down_DropsItsTables_AndUpRestoresThem()
    {
        using var database = PostgresFixture.Start();
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long leader = sql.InsertCharacter(account, Sql.UniqueName("Dn"));
        long member = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Dn"));
        sql.Scalar(Sql.PartyInsert(leader, leader, member));
        const string tables = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' "
            + "AND table_name IN ('parties', 'party_members')";

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, AddCharacterSkills, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        long afterDown = sql.Scalar(tables);
        long characters = sql.Scalar($"SELECT count(*) FROM characters WHERE id IN ({leader}, {member})");
        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That((afterDown, characters), Is.EqualTo((0L, 2L)), "the tables gone, the characters kept");
        Assert.That(sql.Scalar(tables), Is.EqualTo(2));
        Assert.That(sql.Scalar(Sql.PartyInsert(leader, leader, member)), Is.Positive, "and usable again");
    }

    [Test]
    public void CharacterQuest_ActiveOrCompleted_IsStoredOncePerCharacterAndQuest()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "active", 3));
        m_sql.Execute(Sql.QuestInsert(character, "quest.other", "completed", 5));

        AssertFailsWith(UniqueViolation, Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM character_quests WHERE character_id = {character}"),
            Is.EqualTo(2));
    }

    [Test]
    public void CharacterQuest_OfAMissingCharacter_IsRejected()
    {
        AssertFailsWith(ForeignKeyViolation, Sql.QuestInsert(long.MaxValue, "quest.crawler_hunt", "active", 0));
    }

    [Test]
    public void CharacterQuest_WithAQuestIdLongerThanSixtyFour_IsRejectedByTheColumn()
    {
        long character = NewCharacter();

        AssertFailsWith(ValueTooLong, Sql.QuestInsert(character, $"quest.{new string('x', 59)}", "active", 0));
    }

    [Test]
    public void CharacterSkill_FromLevelOneToFive_IsStoredOncePerCharacterAndSkill()
    {
        long character = NewCharacter();
        m_sql.Execute(Sql.SkillInsert(character, "skill.strike", 1));
        m_sql.Execute(Sql.SkillInsert(character, "skill.focus", 5));

        AssertFailsWith(UniqueViolation, Sql.SkillInsert(character, "skill.strike", 2));
        AssertFailsWith(CheckViolation, Sql.SkillInsert(character, "skill.first_aid", 0));
        AssertFailsWith(CheckViolation, Sql.SkillInsert(character, "skill.first_aid", 6));
        AssertFailsWith(ForeignKeyViolation, Sql.SkillInsert(long.MaxValue, "skill.strike", 1));
        AssertFailsWith(ValueTooLong, Sql.SkillInsert(character, $"skill.{new string('x', 59)}", 1));
        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM character_skills WHERE character_id = {character}"),
            Is.EqualTo(2));
    }

    [Test]
    public void Character_AtTheLargestInventoryRevision_IsStored()
    {
        long account = m_sql.InsertAccount();
        string name = Sql.UniqueName("Max");

        long id = m_sql.Scalar(Sql.CharacterInsert(account, name, name.ToLowerInvariant(), uint.MaxValue));

        Assert.That(id, Is.Positive);
    }

    [Test]
    public void Character_InAParty_CannotBeDeleted()
    {
        long leader = NewCharacter();
        m_sql.Scalar(Sql.PartyInsert(leader, leader, NewCharacter()));

        AssertFailsWith(RestrictViolation, $"DELETE FROM characters WHERE id = {leader}");
    }

    [Test]
    public void Character_OfAMissingAccount_IsRejected()
    {
        string name = Sql.UniqueName("Orp");

        AssertFailsWith(ForeignKeyViolation, Sql.CharacterInsert(long.MaxValue, name, name.ToLowerInvariant(), 0));
    }

    [Test]
    public void Character_WhoseNormalizedNameIsNotItsLowerCase_IsRejected()
    {
        long account = m_sql.InsertAccount();
        string name = Sql.UniqueName("Ann");

        AssertFailsWith(CheckViolation, Sql.CharacterInsert(account, name, name, 0));
    }

    [Test]
    public void Character_WithANameLongerThanTwentyThree_IsRejectedByTheColumn()
    {
        long account = m_sql.InsertAccount();
        const string name = "Abcdefghijklmnopqrstuvwx";

        AssertFailsWith(ValueTooLong, Sql.CharacterInsert(account, name, name.ToLowerInvariant(), 0));
    }

    [Test]
    public void Character_WithANameTakenInAnotherCase_IsRejected()
    {
        long first = m_sql.InsertAccount();
        long second = m_sql.InsertAccount();
        string name = Sql.UniqueName("Dup");
        m_sql.InsertCharacter(first, name);
        string otherCase = name.ToUpperInvariant();

        AssertFailsWith(UniqueViolation, Sql.CharacterInsert(second, otherCase, name.ToLowerInvariant(), 0));
    }

    [Test]
    public void Equipment_ForTheCharactersOwnItem_IsStoredOncePerSlot()
    {
        long character = NewCharacter();
        long weapon = m_sql.InsertItem(character, 1);
        long armor = m_sql.InsertItem(character, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        m_sql.Execute(Sql.EquipmentInsert(character, "Armor", armor));

        AssertFailsWith(UniqueViolation, Sql.EquipmentInsert(character, "Weapon", armor));
    }

    [Test]
    public void Equipment_HoldingAnotherCharactersItem_IsRejected()
    {
        long owner = NewCharacter();
        long other = NewCharacter();
        long item = m_sql.InsertItem(owner, 1);

        AssertFailsWith(ForeignKeyViolation, Sql.EquipmentInsert(other, "Weapon", item));
    }

    [Test]
    public void Equipment_HoldingOneItemInTwoSlots_IsRejected()
    {
        long character = NewCharacter();
        long item = m_sql.InsertItem(character, 1);
        m_sql.Execute(Sql.EquipmentInsert(character, "Weapon", item));

        AssertFailsWith(UniqueViolation, Sql.EquipmentInsert(character, "Armor", item));
    }

    [Test]
    public void Equipment_InAnUnknownSlot_IsRejected()
    {
        long character = NewCharacter();
        long item = m_sql.InsertItem(character, 1);

        AssertFailsWith(CheckViolation, Sql.EquipmentInsert(character, "Ring", item));
    }

    [Test]
    public void InventoryItem_OfAMissingCharacter_IsRejected()
    {
        AssertFailsWith(ForeignKeyViolation, Sql.ItemInsert(long.MaxValue, 1));
    }

    // The lead passes in the same transaction as the leader's departure, or the departure is refused at the commit.
    [Test]
    public void Leader_Leaving_IsRejectedUnlessTheLeadPassesInTheSameTransaction()
    {
        long leader = NewCharacter();
        long next = NewCharacter();
        long last = NewCharacter();
        long party = m_sql.Scalar(Sql.PartyInsert(leader, leader, next, last));

        AssertFailsWith(ForeignKeyViolation, $"DELETE FROM party_members WHERE character_id = {leader}");
        m_sql.Execute(
            $"UPDATE parties SET leader_character_id = {next}, version = version + 1 WHERE id = {party}; "
            + $"DELETE FROM party_members WHERE character_id = {leader}");

        Assert.That(m_sql.Scalar($"SELECT leader_character_id FROM parties WHERE id = {party}"), Is.EqualTo(next));
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM party_members WHERE party_id = {party}"), Is.EqualTo(2));
    }

    [Test]
    public void Ledger_RowDelete_IsRejected()
    {
        long entry = NewLedgerEntry();

        AssertFailsWith(RestrictViolation, $"DELETE FROM economy_ledger WHERE id = {entry}");
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM economy_ledger WHERE id = {entry}"), Is.EqualTo(1));
    }

    [Test]
    public void Ledger_RowUpdate_IsRejected()
    {
        long entry = NewLedgerEntry();

        AssertFailsWith(RestrictViolation, $"UPDATE economy_ledger SET quantity_delta = 99 WHERE id = {entry}");
        Assert.That(
            m_sql.Scalar($"SELECT quantity_delta FROM economy_ledger WHERE id = {entry}"),
            Is.EqualTo(1));
    }

    [Test]
    public void Ledger_Truncate_IsRejected()
    {
        long entry = NewLedgerEntry();

        AssertFailsWith(RestrictViolation, "TRUNCATE economy_ledger CASCADE");
        Assert.That(m_sql.Scalar($"SELECT count(*) FROM economy_ledger WHERE id = {entry}"), Is.EqualTo(1));
    }

    [Test]
    public void Ledger_WithARepeatedOperationId_IsRejected()
    {
        long character = NewCharacter();
        var operation = Guid.NewGuid();
        m_sql.Scalar(Sql.LedgerInsert(operation, character, "pickup"));

        AssertFailsWith(UniqueViolation, Sql.LedgerInsert(operation, character, "pickup"));
    }

    [Test]
    public void Ledger_WithAnUnknownOperationType_IsRejected()
    {
        long character = NewCharacter();

        AssertFailsWith(CheckViolation, Sql.LedgerInsert(Guid.NewGuid(), character, "gift"));
    }

    [Test]
    public void Migrations_AppliedFromZero_LeaveNonePending()
    {
        IReadOnlyList<string> pending =
            EvertorchDatabase.GetPendingMigrationsAsync(m_database.ConnectionString, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        Assert.That(pending, Is.Empty);
        Assert.That(
            m_sql.Scalar(
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN "
                + "('accounts', 'characters', 'inventory_items', 'equipment', 'economy_ledger', 'character_quests', "
                + "'session_tokens', 'character_skills', 'parties', 'party_members')"),
            Is.EqualTo(10));
    }

    [Test]
    public void PartyMember_InASecondParty_OrOfNoCharacter_OrWithoutAPlace_IsRejected()
    {
        long leader = NewCharacter();
        long member = NewCharacter();
        long party = m_sql.Scalar(Sql.PartyInsert(leader, leader, member));
        long otherLeader = NewCharacter();
        long other = m_sql.Scalar(Sql.PartyInsert(otherLeader, otherLeader, NewCharacter()));

        AssertFailsWith(UniqueViolation, Sql.PartyMemberInsert(other, member, 2));
        AssertFailsWith(ForeignKeyViolation, Sql.PartyMemberInsert(party, long.MaxValue, 3));
        AssertFailsWith(CheckViolation, Sql.PartyMemberInsert(party, NewCharacter(), 0));
    }

    [Test]
    public void Party_Deleted_TakesItsMembersWithIt()
    {
        long leader = NewCharacter();
        long member = NewCharacter();
        long party = m_sql.Scalar(Sql.PartyInsert(leader, leader, member));

        m_sql.Execute($"DELETE FROM parties WHERE id = {party}");

        Assert.That(
            m_sql.Scalar($"SELECT count(*) FROM party_members WHERE character_id IN ({leader}, {member})"),
            Is.Zero);
    }

    // A party and its first members are written in one transaction; its leader is one of them (Persistence §4).
    [Test]
    public void Party_LedByOneOfItsMembers_IsStored_AndAMemberJoinsLater()
    {
        long leader = NewCharacter();
        long first = NewCharacter();
        long later = NewCharacter();

        long party = m_sql.Scalar(Sql.PartyInsert(leader, leader, first));
        m_sql.Execute(Sql.PartyMemberInsert(party, later, 3));

        Assert.That(
            m_sql.Text(
                "SELECT string_agg(character_id || ':' || join_order, ',' ORDER BY join_order) FROM party_members "
                + $"WHERE party_id = {party}"),
            Is.EqualTo($"{leader}:1,{first}:2,{later}:3"));
        Assert.That(m_sql.Scalar($"SELECT leader_character_id FROM parties WHERE id = {party}"), Is.EqualTo(leader));
    }

    [Test]
    public void Party_WhoseLeaderIsNotAMember_IsRejectedAtTheCommit()
    {
        long outsider = NewCharacter();

        AssertFailsWith(ForeignKeyViolation, Sql.PartyInsert(outsider, NewCharacter(), NewCharacter()));
    }

    [Test]
    public void SessionToken_OfAMissingAccount_IsRejected()
    {
        AssertFailsWith(ForeignKeyViolation, Sql.SessionTokenInsert(long.MaxValue));
    }

    [Test]
    public void SessionToken_WithARepeatedHash_IsRejected()
    {
        long account = m_sql.InsertAccount();
        string insert = "INSERT INTO session_tokens (token_hash, account_id, issued_at, expires_at) "
            + $"VALUES (decode(repeat('ab', 32), 'hex'), {account}, now(), now() + interval '1 minute')";
        m_sql.Execute(insert);

        AssertFailsWith(UniqueViolation, insert);
    }
}
}
