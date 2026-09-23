using System;
using Npgsql;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     Plain SQL against the test database, so constraint tests exercise the schema itself rather than the store's
///     own checks.
/// </summary>
internal sealed class Sql
{
    private readonly string m_connectionString;

    public Sql(string connectionString)
    {
        m_connectionString = connectionString;
    }

    /// <summary>
    ///     A name of letters and digits that no other test uses.
    /// </summary>
    public static string UniqueName(string prefix)
    {
        return $"{prefix}{Guid.NewGuid():N}".Substring(0, 12);
    }

    public int Execute(string commandText)
    {
        using var connection = new NpgsqlConnection(m_connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(commandText, connection);
        return command.ExecuteNonQuery();
    }

    public long Scalar(string commandText)
    {
        using var connection = new NpgsqlConnection(m_connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(commandText, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public long InsertAccount()
    {
        return Scalar(
            "INSERT INTO accounts (login_normalized, status, created_at) "
            + $"VALUES ('dev:{Guid.NewGuid():N}', 'active', now()) RETURNING id");
    }

    public long InsertCharacter(long account, string name)
    {
        return Scalar(CharacterInsert(account, name, name.ToLowerInvariant(), 0));
    }

    public static string CharacterInsert(long account, string name, string normalized, long revision)
    {
        return "INSERT INTO characters (account_id, name, name_normalized, job_definition_id, base_level, job_level, "
            + "base_exp, job_exp, str, agi, vit, \"int\", dex, luk, hp, sp, currency, map_definition_id, "
            + "position_x, position_y, position_z, inventory_revision, created_at, version) "
            + $"VALUES ({account}, '{name}', '{normalized}', 'job.adventurer', 1, 1, 0, 0, 5, 5, 5, 5, 5, 5, 60, 20, 0, "
            + $"'map.training_ground', 0, 0, 0, {revision}, now(), 0) RETURNING id";
    }

    public long InsertItem(long character, int quantity)
    {
        return Scalar(ItemInsert(character, quantity));
    }

    public static string ItemInsert(long character, int quantity)
    {
        return "INSERT INTO inventory_items (character_id, item_definition_id, quantity, refine_level, version) "
            + $"VALUES ({character}, 'item.material.slime_gel', {quantity}, 0, 0) RETURNING id";
    }

    public static string EquipmentInsert(long character, string slot, long item)
    {
        return "INSERT INTO equipment (character_id, slot, inventory_item_id, version) "
            + $"VALUES ({character}, '{slot}', {item}, 0)";
    }

    public static string LedgerInsert(Guid operation, long? actor, string operationType)
    {
        string actorText = actor.HasValue ? actor.Value.ToString() : "NULL";
        return "INSERT INTO economy_ledger (operation_id, actor_character_id, operation_type, item_definition_id, "
            + "quantity_delta, currency_delta, created_at) "
            + $"VALUES ('{operation}', {actorText}, '{operationType}', 'item.material.slime_gel', 1, 0, now()) "
            + "RETURNING id";
    }
}
}
