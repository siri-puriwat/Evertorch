using System.Collections.Generic;
using Evertorch.Game;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class WorldOptions
{
    public const string SectionName = "World";

    /// <summary>
    ///     The job every character enters the world with until persisted characters exist.
    /// </summary>
    public string StartingJob { get; set; } = "job.adventurer";

    /// <summary>
    ///     Side of one interest cell in world units.
    /// </summary>
    public float InterestCellSize { get; set; } = 16f;

    /// <summary>
    ///     How many cells around its own a player is told about: 1 means the surrounding 3 × 3 block.
    /// </summary>
    public int InterestNeighborRadius { get; set; } = 1;

    /// <summary>
    ///     How long the last movement input keeps applying when no newer one arrives, before the entity is stopped.
    /// </summary>
    public int InputHoldTimeoutMs { get; set; } = 250;

    /// <summary>
    ///     Inputs kept per player for later ticks. When more arrive the oldest is discarded. Kept small on purpose:
    ///     one input is consumed per tick and a moving client sends one per tick, so whatever backlog a burst leaves
    ///     never drains while the player keeps moving, and every queued input is 50 ms by which a stop comes late.
    /// </summary>
    public int MaxQueuedInputs { get; set; } = 3;

    /// <summary>
    ///     How far, in ticks, a client's tick may wander from the offset first observed before it is measured anew.
    /// </summary>
    public int MaxClientTickDrift { get; set; } = 40;

    /// <summary>
    ///     A snapshot is sent every this many ticks.
    /// </summary>
    public int SnapshotIntervalTicks { get; set; } = 1;
}

internal sealed class WorldOptionsValidator : IValidateOptions<WorldOptions>
{
    public ValidateOptionsResult Validate(string? name, WorldOptions options)
    {
        var failures = new List<string>();
        if (!JobDefinitionId.TryCreate(options.StartingJob, out JobDefinitionId _))
        {
            failures.Add($"{WorldOptions.SectionName}:StartingJob must be a job definition ID.");
        }

        if (!(options.InterestCellSize >= 1f && options.InterestCellSize <= 1024f))
        {
            failures.Add($"{WorldOptions.SectionName}:InterestCellSize must be between 1 and 1024.");
        }

        if (options.InterestNeighborRadius < 0 || options.InterestNeighborRadius > 8)
        {
            failures.Add($"{WorldOptions.SectionName}:InterestNeighborRadius must be between 0 and 8.");
        }

        AddRangeFailure(failures, "InputHoldTimeoutMs", options.InputHoldTimeoutMs, 0, 5000);
        AddRangeFailure(failures, "MaxQueuedInputs", options.MaxQueuedInputs, 1, 64);
        AddRangeFailure(failures, "MaxClientTickDrift", options.MaxClientTickDrift, 1, 100000);
        AddRangeFailure(failures, "SnapshotIntervalTicks", options.SnapshotIntervalTicks, 1, 120);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(List<string> failures, string key, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{WorldOptions.SectionName}:{key} must be between {minimum} and {maximum}.");
        }
    }
}
}
