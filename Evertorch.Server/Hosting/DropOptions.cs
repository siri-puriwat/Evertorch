using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The drop tables' options (Gameplay Systems §10; System Architecture §12): a multiple of every card's drop
///     chance, capped at 1, which a walk and the live tests raise. It moves thresholds only, never the number of draws,
///     so a seed replays the same draws at any scale.
/// </summary>
public sealed class DropOptions
{
    public const string SectionName = "Drops";

    public const int MaxCardChanceScale = 1000;

    public int CardChanceScale { get; set; } = 1;
}

public sealed class DropOptionsValidator : IValidateOptions<DropOptions>
{
    public ValidateOptionsResult Validate(string? name, DropOptions options)
    {
        var failures = new List<string>();
        if (options.CardChanceScale < 1 || options.CardChanceScale > DropOptions.MaxCardChanceScale)
        {
            failures.Add(
                $"{DropOptions.SectionName}:CardChanceScale must be between 1 and {DropOptions.MaxCardChanceScale}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
