using System.Collections.Generic;
using Evertorch.Game;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class WorldOptions
{
    public const string SectionName = "World";

    /// <summary>
    /// The job every character enters the world with until persisted characters exist.
    /// </summary>
    public string StartingJob { get; set; } = "job.adventurer";

    /// <summary>
    /// Side of one interest cell in world units.
    /// </summary>
    public float InterestCellSize { get; set; } = 16f;

    /// <summary>
    /// How many cells around its own a player is told about: 1 means the surrounding 3 × 3 block.
    /// </summary>
    public int InterestNeighborRadius { get; set; } = 1;
}

internal sealed class WorldOptionsValidator : IValidateOptions<WorldOptions>
{
    public ValidateOptionsResult Validate(string? name, WorldOptions options)
    {
        List<string> failures = new List<string>();
        if (!JobDefinitionId.TryCreate(options.StartingJob, out JobDefinitionId _))
        {
            failures.Add(WorldOptions.SectionName + ":StartingJob must be a job definition ID.");
        }

        if (!(options.InterestCellSize >= 1f && options.InterestCellSize <= 1024f))
        {
            failures.Add(WorldOptions.SectionName + ":InterestCellSize must be between 1 and 1024.");
        }

        if (options.InterestNeighborRadius < 0 || options.InterestNeighborRadius > 8)
        {
            failures.Add(WorldOptions.SectionName + ":InterestNeighborRadius must be between 0 and 8.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
