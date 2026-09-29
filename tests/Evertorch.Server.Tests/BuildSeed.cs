using System;
using System.Collections.Generic;
using Npgsql;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The build a test character is given in its stored row before it first enters, for tests that need what a new
///     character has not earned (Gameplay Systems §2.1, §9).
/// </summary>
internal sealed class BuildSeed
{
    public const string AdventurerJob = "job.adventurer";

    /// <summary>
    ///     The build the Milestone 6 and 7 suites play with: job level 6, whose five skill points buy Strike 1, First
    ///     Aid 1, and Focus 3, the levels whose values those suites know.
    /// </summary>
    public static readonly BuildSeed Adventurer = new(
        6,
        new Dictionary<string, int> { ["skill.strike"] = 1, ["skill.first_aid"] = 1, ["skill.focus"] = 3 });

    /// <summary>
    ///     An Adventurer at its job cap, where the Guildmaster changes jobs (Gameplay Systems §6.1): job level 10, whose
    ///     nine skill points bought Strike 1, First Aid 1, and Focus 2, so five are left unspent to carry over.
    /// </summary>
    public static readonly BuildSeed ReadyToChange = new(
        10,
        new Dictionary<string, int> { ["skill.strike"] = 1, ["skill.first_aid"] = 1, ["skill.focus"] = 2 });

    public BuildSeed(int jobLevel, IReadOnlyDictionary<string, int> skills, string job = AdventurerJob)
    {
        JobLevel = jobLevel;
        Skills = skills;
        Job = job;
    }

    public int JobLevel { get; }

    /// <summary>
    ///     The job's ID; the Adventurer's unless a test seeds a first job.
    /// </summary>
    public string Job { get; }

    /// <summary>
    ///     Each learned skill's level, by skill ID.
    /// </summary>
    public IReadOnlyDictionary<string, int> Skills { get; }

    /// <summary>
    ///     Writes the build into the stored rows of the character named <paramref name="name" />.
    /// </summary>
    public void Apply(string connectionString, string name)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var update = new NpgsqlCommand(
            "UPDATE characters SET job_definition_id = @job, job_level = @jobLevel, job_exp = 0 WHERE name = @name "
            + "RETURNING id",
            connection);
        update.Parameters.AddWithValue("job", Job);
        update.Parameters.AddWithValue("jobLevel", JobLevel);
        update.Parameters.AddWithValue("name", name);
        object? id = update.ExecuteScalar();
        if (id == null)
        {
            throw new InvalidOperationException($"No stored character {name} to seed.");
        }

        foreach (KeyValuePair<string, int> skill in Skills)
        {
            using var insert = new NpgsqlCommand(
                "INSERT INTO character_skills (character_id, skill_definition_id, level) VALUES (@id, @skill, @level)",
                connection);
            insert.Parameters.AddWithValue("id", (long)id);
            insert.Parameters.AddWithValue("skill", skill.Key);
            insert.Parameters.AddWithValue("level", skill.Value);
            insert.ExecuteNonQuery();
        }
    }
}
}
