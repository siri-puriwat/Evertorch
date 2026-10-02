using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Npgsql;
using NUnit.Framework;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A database an older migration made upgrades to the latest without losing anything (Persistence §4, §11). Each
///     later migration adds its own case here.
/// </summary>
[TestFixture]
public sealed class MigrationUpgradeTests
{
    private const string InitialSchema = "20260923125025_InitialSchema";
    private const string WidenLedgerOperationTypes = "20260926015421_WidenLedgerOperationTypes";
    private const string AddQuestsAndCoins = "20260926193354_AddQuestsAndCoins";
    private const string AddSessionTokens = "20260927150302_AddSessionTokens";
    private const string AddCharacterSkills = "20260928181417_AddCharacterSkills";
    private const string AddParties = "20261001131541_AddParties";
    private const string ForeignKeyViolation = "23503";
    private const string CheckViolation = "23514";
    private const string UndefinedTable = "42P01";

    private static readonly string[] Tables =
        { "accounts", "characters", "inventory_items", "equipment", "economy_ledger" };

    // Every row of every table, in a fixed order, as its JSON.
    private static string Dump(Sql sql, params string[] moreTables)
    {
        return string.Join(
            "\n",
            Tables.Concat(moreTables).Select(table => sql.Text(
                "SELECT coalesce(string_agg(row_to_json(t)::text, '|' ORDER BY row_to_json(t)::text), '') "
                + $"FROM {table} t")));
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

    // Every existing character starts without a party (Persistence §4; owner decision 3 of the pre-Milestone-12
    // review): the migration changes no row.
    [Test]
    public void Upgrade_FromAddCharacterSkills_KeepsEveryRow_AndAddsParties()
    {
        using var database = PostgresFixture.Start(targetMigration: AddCharacterSkills);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "buy"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "active", 3));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        string? partyBefore = SqlStateOf(sql, Sql.PartyInsert(character, character, other));
        string before = Dump(sql, "character_quests", "session_tokens", "character_skills");

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(partyBefore, Is.EqualTo(UndefinedTable), "no parties before");
        Assert.That(pending, Is.Empty);
        Assert.That(
            Dump(sql, "character_quests", "session_tokens", "character_skills"),
            Is.EqualTo(before),
            "every row as it was");
        Assert.That(sql.Scalar("SELECT count(*) FROM party_members"), Is.Zero, "nobody in a party");
        Assert.That(SqlStateOf(sql, Sql.PartyInsert(character, character, other)), Is.Null);
        Assert.That(
            SqlStateOf(sql, Sql.PartyInsert(other, sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up")))),
            Is.EqualTo(ForeignKeyViolation),
            "the leader's key holds");
    }

    // A boss's prize joins the ledger's operation types (Persistence §4, §5); the migration changes no row.
    [Test]
    public void Upgrade_FromAddParties_KeepsEveryRow_AndAcceptsBossRewards()
    {
        using var database = PostgresFixture.Start(targetMigration: AddParties);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "quest_reward"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        string? rewardBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "boss_reward"));
        string before = Dump(sql, "character_quests", "session_tokens", "character_skills", "parties", "party_members");

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(rewardBefore, Is.EqualTo(CheckViolation), "the ledger knew no prizes before");
        Assert.That(pending, Is.Empty);
        Assert.That(
            Dump(sql, "character_quests", "session_tokens", "character_skills", "parties", "party_members"),
            Is.EqualTo(before),
            "every row as it was");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "boss_reward")), Is.Null);
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "trade")), Is.EqualTo(CheckViolation));
    }

    [Test]
    public void Upgrade_FromAddQuestsAndCoins_KeepsEveryRow_AddsSessionTokensAndThePasswordCheck()
    {
        using var database = PostgresFixture.Start(targetMigration: AddQuestsAndCoins);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "buy"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "active", 3));
        sql.Execute($"UPDATE characters SET currency = 250 WHERE id = {character}");
        string? tokenBefore = SqlStateOf(sql, Sql.SessionTokenInsert(account));
        string before = Dump(sql, "character_quests");

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(tokenBefore, Is.EqualTo(UndefinedTable), "no session tokens before");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, "character_quests"), Is.EqualTo(before), "every row as it was");
        Assert.That(SqlStateOf(sql, Sql.SessionTokenInsert(account)), Is.Null, "an existing account takes tokens");
        Assert.That(SqlStateOf(sql, Sql.SessionTokenInsert(account, 31)), Is.EqualTo(CheckViolation));
        Assert.That(
            SqlStateOf(sql, $"UPDATE accounts SET password_hash = '1000$c2FsdA==$a2V5' WHERE id = {account}"),
            Is.EqualTo(CheckViolation),
            "a hash needs its scheme");
        Assert.That(
            SqlStateOf(
                sql,
                "UPDATE accounts SET password_hash = '1000$c2FsdA==$a2V5', password_scheme = 'pbkdf2-sha256' "
                + $"WHERE id = {account}"),
            Is.Null);
    }

    // Every existing character stays at job level 1 with no learned skill (Persistence §4; owner decision 8 of the
    // pre-Milestone-9 review): the migration changes no row.
    [Test]
    public void Upgrade_FromAddSessionTokens_KeepsEveryRow_AndAddsLearnedSkills()
    {
        using var database = PostgresFixture.Start(targetMigration: AddSessionTokens);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "buy"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "active", 3));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(
            $"UPDATE characters SET currency = 250, base_level = 7, base_exp = 12, str = 11 WHERE id = {character}");
        string? skillBefore = SqlStateOf(sql, Sql.SkillInsert(character, "skill.strike", 1));
        string before = Dump(sql, "character_quests", "session_tokens");

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(skillBefore, Is.EqualTo(UndefinedTable), "no learned skills before");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, "character_quests", "session_tokens"), Is.EqualTo(before), "every row as it was");
        Assert.That(
            sql.Text($"SELECT job_level || ' ' || job_exp FROM characters WHERE id = {character}"),
            Is.EqualTo("1 0"),
            "still job level 1");
        Assert.That(sql.Scalar("SELECT count(*) FROM character_skills"), Is.Zero, "nothing learned");
        Assert.That(SqlStateOf(sql, Sql.SkillInsert(character, "skill.strike", 1)), Is.Null);
        Assert.That(SqlStateOf(sql, Sql.SkillInsert(character, "skill.focus", 6)), Is.EqualTo(CheckViolation));
    }

    [Test]
    public void Upgrade_FromInitialSchema_KeepsEveryRow_AndWidensTheLedgersOperationTypes()
    {
        using var database = PostgresFixture.Start(targetMigration: InitialSchema);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        sql.InsertItem(character, 7);
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "pickup"));
        string? equipBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "equip"));
        string before = Dump(sql);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(equipBefore, Is.EqualTo(CheckViolation), "InitialSchema knew only pickups");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql), Is.EqualTo(before), "every row as it was");
        Assert.That(
            new[] { "equip", "unequip", "consume" }
                .Select(type => SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, type))),
            Is.All.Null,
            "the new operation types are accepted");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "trade")), Is.EqualTo(CheckViolation));
    }

    [Test]
    public void Upgrade_FromWidenLedgerOperationTypes_KeepsEveryRow_AddsQuestsTheShopsTypesAndTheCoinCap()
    {
        using var database = PostgresFixture.Start(targetMigration: WidenLedgerOperationTypes);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long gel = sql.InsertItem(character, 7);
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "pickup"));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "equip"));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "consume"));
        sql.Execute($"UPDATE characters SET currency = 250 WHERE id = {character}");
        string? buyBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "buy"));
        string before = Dump(sql);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(buyBefore, Is.EqualTo(CheckViolation), "the ledger knew no buys before");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql), Is.EqualTo(before), "every row as it was");
        Assert.That(sql.Scalar($"SELECT quantity FROM inventory_items WHERE id = {gel}"), Is.EqualTo(7));
        Assert.That(
            new[] { "buy", "sell", "quest_reward" }
                .Select(type => SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, type))),
            Is.All.Null,
            "the new operation types are accepted");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "trade")), Is.EqualTo(CheckViolation));
        Assert.That(
            SqlStateOf(sql, $"UPDATE characters SET currency = 1000000001 WHERE id = {character}"),
            Is.EqualTo(CheckViolation),
            "the coin cap holds");
        Assert.That(SqlStateOf(sql, $"UPDATE characters SET currency = 1000000000 WHERE id = {character}"), Is.Null);
        Assert.That(SqlStateOf(sql, Sql.QuestInsert(character, "quest.crawler_hunt", "active", 0)), Is.Null);
    }
}
}
