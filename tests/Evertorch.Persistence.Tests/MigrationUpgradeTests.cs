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
    private const string CheckViolation = "23514";

    private static readonly string[] Tables =
        { "accounts", "characters", "inventory_items", "equipment", "economy_ledger" };

    // Every row of every table, in a fixed order, as its JSON.
    private static string Dump(Sql sql)
    {
        return string.Join(
            "\n",
            Tables.Select(table => sql.Text(
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
}
}
