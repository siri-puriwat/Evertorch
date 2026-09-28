using System;
using Npgsql;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The build a test character is given in its stored row before it first enters, for tests that need what a new
///     character has not earned (Gameplay Systems §2.1, §9).
/// </summary>
internal sealed class BuildSeed
{
    /// <summary>
    ///     The build the Milestone 6 and 7 suites play with: job level 6, whose five skill points buy Strike 1, First
    ///     Aid 1, and Focus 3.
    /// </summary>
    public static readonly BuildSeed Adventurer = new(6);

    public BuildSeed(int jobLevel)
    {
        JobLevel = jobLevel;
    }

    public int JobLevel { get; }

    /// <summary>
    ///     Writes the build into the stored row of the character named <paramref name="name" />.
    /// </summary>
    public void Apply(string connectionString, string name)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "UPDATE characters SET job_level = @jobLevel, job_exp = 0 WHERE name = @name",
            connection);
        command.Parameters.AddWithValue("jobLevel", JobLevel);
        command.Parameters.AddWithValue("name", name);
        int updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"No stored character {name} to seed.");
        }
    }
}
}
