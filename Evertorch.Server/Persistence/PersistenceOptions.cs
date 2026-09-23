using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The bridge between the tick thread and the database (Persistence §9).
/// </summary>
public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>
    ///     Jobs that may wait for the writer. A durable command that finds the queue full is rejected as retryable.
    ///     Checkpoints do not count: they wait in one slot per character.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>
    ///     The longest one job may take, retries and backoff included.
    /// </summary>
    public int CommandTimeoutMs { get; set; } = 5000;

    /// <summary>
    ///     Retries of a job whose database was unreachable or too slow.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    ///     The first retry waits up to this long; each later one up to twice the previous bound.
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 100;

    /// <summary>
    ///     How often a character in the world is checkpointed.
    /// </summary>
    public int CheckpointIntervalMs { get; set; } = 60000;
}

public sealed class PersistenceOptionsValidator : IValidateOptions<PersistenceOptions>
{
    public ValidateOptionsResult Validate(string? name, PersistenceOptions options)
    {
        var failures = new List<string>();
        AddRangeFailure(failures, "QueueCapacity", options.QueueCapacity, 1, 100000);
        AddRangeFailure(failures, "CommandTimeoutMs", options.CommandTimeoutMs, 100, 60000);
        AddRangeFailure(failures, "MaxRetries", options.MaxRetries, 0, 10);
        AddRangeFailure(failures, "RetryBaseDelayMs", options.RetryBaseDelayMs, 1, 10000);
        AddRangeFailure(failures, "CheckpointIntervalMs", options.CheckpointIntervalMs, 1000, 3600000);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(List<string> failures, string key, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{PersistenceOptions.SectionName}:{key} must be between {minimum} and {maximum}.");
        }
    }
}
}
