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
    private const string AddBossRewards = "20261002222843_AddBossRewards";
    private const string AddTradesAndStorage = "20261003172540_AddTradesAndStorage";
    private const string AddMarketBoard = "20261010111957_AddMarketBoard";
    private const string AddRefining = "20261011013231_AddRefining";
    private const string AddCards = "20261012164717_AddCards";
    private const string AddSavedStatusEffects = "20261014172003_AddSavedStatusEffects";
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

    // Trades and storage add three tables and three ledger types (Persistence §4); the migration changes no row, and
    // every account starts with no storage.
    [Test]
    public void Upgrade_FromAddBossRewards_KeepsEveryRow_AndAddsTradesAndStorage()
    {
        using var database = PostgresFixture.Start(targetMigration: AddBossRewards);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "boss_reward"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        string? tradeBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "trade"));
        string? tableBefore = SqlStateOf(sql, "SELECT count(*) FROM storage_items");
        string before = Dump(sql, "character_quests", "session_tokens", "character_skills", "parties", "party_members");

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((tradeBefore, tableBefore), Is.EqualTo((CheckViolation, UndefinedTable)), "neither existed");
        Assert.That(pending, Is.Empty);
        Assert.That(
            Dump(sql, "character_quests", "session_tokens", "character_skills", "parties", "party_members"),
            Is.EqualTo(before),
            "every row as it was");
        Assert.That(
            sql.Scalar(
                "SELECT (SELECT count(*) FROM trades) + (SELECT count(*) FROM account_storages) "
                + "+ (SELECT count(*) FROM storage_items)"),
            Is.Zero,
            "no trade and no storage yet");
        foreach (string type in new[] { "trade", "storage_deposit", "storage_withdraw" })
        {
            Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, type)), Is.Null, type);
        }

        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
    }

    // Saved statuses add one table (Persistence §4); the migration changes no row, and no character has a saved
    // status until its next checkpoint.
    [Test]
    public void Upgrade_FromAddCards_KeepsEveryRow_AndAddsSavedStatusEffects()
    {
        using var database = PostgresFixture.Start(targetMigration: AddCards);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version, "
            + "instance_data_json) VALUES "
            + $"({character}, 'item.weapon.iron_sword', 1, 7, 0, '{{\"cards\": [\"item.card.forest_crawler\"]}}') "
            + "RETURNING id");
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "card_insert"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        sql.Execute($"INSERT INTO account_storages (account_id, revision, version) VALUES ({account}, 1, 0)");
        sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({account}, 'item.armor.cloth', 1, 4, 0)");
        long listing = sql.Scalar(Sql.ListingInsert(character, "item.material.gloom_dust", 5, 3, 56));
        sql.Execute(Sql.SaleInsert(Guid.NewGuid(), listing, character, other, 2, 56));
        sql.Execute(Sql.RefineAttemptInsert(Guid.NewGuid(), character, "refined", 6, "7"));
        sql.Execute(Sql.CardChangeInsert(Guid.NewGuid(), character, "insert", 1));
        string? tableBefore = SqlStateOf(sql, "SELECT count(*) FROM character_status_effects");
        string[] more =
        {
            "character_quests", "session_tokens", "character_skills", "parties", "party_members",
            "account_storages", "storage_items", "market_listings", "market_sales", "refine_attempts", "card_changes"
        };
        string before = Dump(sql, more);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(tableBefore, Is.EqualTo(UndefinedTable), "no table before");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, more), Is.EqualTo(before), "every row as it was");
        Assert.That(sql.Scalar("SELECT count(*) FROM character_status_effects"), Is.Zero, "no saved status yet");
        Assert.That(SqlStateOf(sql, Sql.StatusEffectInsert(character, "status.focus", 30_000, 100)), Is.Null);
        Assert.That(
            SqlStateOf(sql, Sql.StatusEffectInsert(other, "status.focus", 0, 100)),
            Is.EqualTo(CheckViolation),
            "the new table's check");
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

    // Refining adds one table and one ledger type and bounds the refine levels (Persistence §4); the migration changes
    // no row, a stored level is kept wherever its row is, and the ledger stays append-only.
    [Test]
    public void Upgrade_FromAddMarketBoard_KeepsEveryRow_AndAddsRefineAttempts()
    {
        using var database = PostgresFixture.Start(targetMigration: AddMarketBoard);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, 'item.weapon.training_sword', 1, 7, 0) RETURNING id");
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "market_buy"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        sql.Execute($"INSERT INTO account_storages (account_id, revision, version) VALUES ({account}, 1, 0)");
        sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({account}, 'item.armor.cloth', 1, 4, 0)");
        long listing = sql.Scalar(Sql.ListingInsert(character, "item.material.gloom_dust", 5, 3, 56));
        sql.Execute(Sql.SaleInsert(Guid.NewGuid(), listing, character, other, 2, 56));
        sql.Execute(
            "INSERT INTO market_prices (item_definition_id, price, listed_ms, settled_at, version) "
            + "VALUES ('item.material.gloom_dust', 28, 0, now(), 0)");
        string? refineBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "refine"));
        string? tableBefore = SqlStateOf(sql, "SELECT count(*) FROM refine_attempts");
        string[] more =
        {
            "character_quests", "session_tokens", "character_skills", "parties", "party_members",
            "account_storages", "storage_items", "market_listings", "market_sales", "market_prices"
        };
        string before = Dump(sql, more);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((refineBefore, tableBefore), Is.EqualTo((CheckViolation, UndefinedTable)), "neither existed");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, more), Is.EqualTo(before), "every row as it was, the levels 7 and 4 kept");
        Assert.That(sql.Scalar("SELECT count(*) FROM refine_attempts"), Is.Zero, "no attempt yet");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "refine")), Is.Null, "refine");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
        Assert.That(
            SqlStateOf(sql, $"UPDATE inventory_items SET refine_level = 256 WHERE id = {weapon}"),
            Is.EqualTo(CheckViolation),
            "the levels' new bound");
        Assert.That(
            SqlStateOf(sql, $"DELETE FROM economy_ledger WHERE actor_character_id = {character}"),
            Is.EqualTo("23001"),
            "still append-only");
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
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
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

    // Cards add one table and two ledger types and bound the instance data (Persistence §4); the migration changes no
    // row, every row so far holds none, and the ledger stays append-only.
    [Test]
    public void Upgrade_FromAddRefining_KeepsEveryRow_AndAddsCardChanges()
    {
        using var database = PostgresFixture.Start(targetMigration: AddRefining);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, 'item.weapon.iron_sword', 1, 7, 0) RETURNING id");
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "refine"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        sql.Execute($"INSERT INTO account_storages (account_id, revision, version) VALUES ({account}, 1, 0)");
        sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({account}, 'item.armor.cloth', 1, 4, 0)");
        long listing = sql.Scalar(Sql.ListingInsert(character, "item.material.gloom_dust", 5, 3, 56));
        sql.Execute(Sql.SaleInsert(Guid.NewGuid(), listing, character, other, 2, 56));
        sql.Execute(Sql.RefineAttemptInsert(Guid.NewGuid(), character, "refined", 6, "7"));
        string? cardBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "card_insert"));
        string? tableBefore = SqlStateOf(sql, "SELECT count(*) FROM card_changes");
        string[] more =
        {
            "character_quests", "session_tokens", "character_skills", "parties", "party_members",
            "account_storages", "storage_items", "market_listings", "market_sales", "refine_attempts"
        };
        string before = Dump(sql, more);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((cardBefore, tableBefore), Is.EqualTo((CheckViolation, UndefinedTable)), "neither existed");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, more), Is.EqualTo(before), "every row as it was, with no cards");
        Assert.That(sql.Scalar("SELECT count(*) FROM card_changes"), Is.Zero, "no change yet");
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "card_insert")), Is.Null);
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "card_remove")), Is.Null);
        Assert.That(
            SqlStateOf(sql, $"UPDATE inventory_items SET instance_data_json = '{{}}' WHERE id = {weapon}"),
            Is.EqualTo(CheckViolation),
            "the cards' new check");
        Assert.That(
            SqlStateOf(
                sql,
                "UPDATE inventory_items SET instance_data_json = '{\"cards\": [\"item.card.forest_crawler\"]}' "
                + $"WHERE id = {weapon}"),
            Is.Null,
            "a worn row may hold cards");
        Assert.That(
            SqlStateOf(sql, $"DELETE FROM economy_ledger WHERE actor_character_id = {character}"),
            Is.EqualTo("23001"),
            "still append-only");
    }

    // The eight positions widen one check (Persistence §4): the migration changes no row, refined and carded gear stays
    // worn where it was, and saved statuses stay.
    [Test]
    public void Upgrade_FromAddSavedStatusEffects_KeepsEveryRow_AndWidensTheEquipmentSlots()
    {
        using var database = PostgresFixture.Start(targetMigration: AddSavedStatusEffects);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version, "
            + "instance_data_json) VALUES "
            + $"({character}, 'item.weapon.iron_sword', 1, 7, 0, '{{\"cards\": [\"item.card.forest_crawler\"]}}') "
            + "RETURNING id");
        long armor = sql.Scalar(
            "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version, "
            + "instance_data_json) VALUES "
            + $"({character}, 'item.armor.leather', 1, 4, 0, '{{\"cards\": [\"item.card.training_slime\"]}}') "
            + "RETURNING id");
        long unworn = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Execute(Sql.EquipmentInsert(character, "Armor", armor));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "equip"));
        sql.Execute(Sql.StatusEffectInsert(character, "status.focus", 30_000, 100));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        sql.Execute($"INSERT INTO account_storages (account_id, revision, version) VALUES ({account}, 1, 0)");
        sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({account}, 'item.armor.cloth', 1, 4, 0)");
        long listing = sql.Scalar(Sql.ListingInsert(character, "item.material.gloom_dust", 5, 3, 56));
        sql.Execute(Sql.SaleInsert(Guid.NewGuid(), listing, character, other, 2, 56));
        sql.Execute(Sql.RefineAttemptInsert(Guid.NewGuid(), character, "refined", 6, "7"));
        sql.Execute(Sql.CardChangeInsert(Guid.NewGuid(), character, "insert", 1));
        string? headgearBefore = SqlStateOf(sql, Sql.EquipmentInsert(character, "Headgear", unworn));
        string[] more =
        {
            "character_quests", "session_tokens", "character_skills", "parties", "party_members",
            "account_storages", "storage_items", "market_listings", "market_sales", "refine_attempts", "card_changes",
            "character_status_effects"
        };
        string before = Dump(sql, more);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(headgearBefore, Is.EqualTo(CheckViolation), "two slots before");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, more), Is.EqualTo(before), "every row as it was");
        Assert.That(SqlStateOf(sql, Sql.EquipmentInsert(character, "Headgear", unworn)), Is.Null, "a headgear now");
        Assert.That(
            SqlStateOf(sql, Sql.EquipmentInsert(character, "Accessory2", sql.InsertItem(character, 1))),
            Is.Null,
            "and the second accessory");
        Assert.That(
            SqlStateOf(sql, Sql.EquipmentInsert(character, "Ring", sql.InsertItem(character, 1))),
            Is.EqualTo(CheckViolation),
            "but no ninth name");
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

    // The market board adds three tables and three ledger types (Persistence §4); the migration changes no row, the
    // board starts empty, and the ledger stays append-only.
    [Test]
    public void Upgrade_FromAddTradesAndStorage_KeepsEveryRow_AndAddsTheMarketBoard()
    {
        using var database = PostgresFixture.Start(targetMigration: AddTradesAndStorage);
        var sql = new Sql(database.ConnectionString);
        long account = sql.InsertAccount();
        long character = sql.InsertCharacter(account, Sql.UniqueName("Up"));
        long other = sql.InsertCharacter(sql.InsertAccount(), Sql.UniqueName("Up"));
        long weapon = sql.InsertItem(character, 1);
        sql.Execute(Sql.EquipmentInsert(character, "Weapon", weapon));
        sql.Scalar(Sql.LedgerInsert(Guid.NewGuid(), character, "trade"));
        sql.Execute(Sql.QuestInsert(character, "quest.crawler_hunt", "completed", 5));
        sql.Execute(Sql.SessionTokenInsert(account));
        sql.Execute(Sql.SkillInsert(character, "skill.strike", 2));
        sql.Scalar(Sql.PartyInsert(character, character, other));
        sql.Execute(
            "INSERT INTO trades (id, first_character_id, second_character_id, committed_at) "
            + $"VALUES ('{Guid.NewGuid()}', {Math.Min(character, other)}, {Math.Max(character, other)}, now())");
        sql.Execute($"INSERT INTO account_storages (account_id, revision, version) VALUES ({account}, 1, 0)");
        sql.Execute(
            "INSERT INTO storage_items (account_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({account}, 'item.material.slime_gel', 4, 0, 0)");
        string? listBefore = SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "market_list"));
        string? tableBefore = SqlStateOf(sql, "SELECT count(*) FROM market_listings");
        string[] more =
        {
            "character_quests", "session_tokens", "character_skills", "parties", "party_members", "trades",
            "account_storages", "storage_items"
        };
        string before = Dump(sql, more);

        EvertorchDatabase.ApplyMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        IReadOnlyList<string> pending = EvertorchDatabase
            .GetPendingMigrationsAsync(database.ConnectionString, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That((listBefore, tableBefore), Is.EqualTo((CheckViolation, UndefinedTable)), "neither existed");
        Assert.That(pending, Is.Empty);
        Assert.That(Dump(sql, more), Is.EqualTo(before), "every row as it was");
        Assert.That(
            sql.Scalar(
                "SELECT (SELECT count(*) FROM market_prices) + (SELECT count(*) FROM market_listings) "
                + "+ (SELECT count(*) FROM market_sales)"),
            Is.Zero,
            "an empty board");
        foreach (string type in new[] { "market_list", "market_buy", "market_collect" })
        {
            Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, type)), Is.Null, type);
        }

        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
        Assert.That(
            SqlStateOf(sql, $"DELETE FROM economy_ledger WHERE actor_character_id = {character}"),
            Is.EqualTo("23001"),
            "still append-only");
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
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
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
        Assert.That(SqlStateOf(sql, Sql.LedgerInsert(Guid.NewGuid(), character, "gift")), Is.EqualTo(CheckViolation));
        Assert.That(
            SqlStateOf(sql, $"UPDATE characters SET currency = 1000000001 WHERE id = {character}"),
            Is.EqualTo(CheckViolation),
            "the coin cap holds");
        Assert.That(SqlStateOf(sql, $"UPDATE characters SET currency = 1000000000 WHERE id = {character}"), Is.Null);
        Assert.That(SqlStateOf(sql, Sql.QuestInsert(character, "quest.crawler_hunt", "active", 0)), Is.Null);
    }
}
}
