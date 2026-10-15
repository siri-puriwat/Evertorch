using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Combat's options (Gameplay Systems §9.1; System Architecture §12): a multiple of every ailment's chance before
///     resistance, capped at certain, which a walk and the live tests raise. It moves thresholds only, never the number
///     of draws, so a seed replays the same draws at any scale.
/// </summary>
public sealed class CombatOptions
{
    public const string SectionName = "Combat";

    public const int MaxAilmentChanceScale = 100;

    public int AilmentChanceScale { get; set; } = 1;
}

public sealed class CombatOptionsValidator : IValidateOptions<CombatOptions>
{
    public ValidateOptionsResult Validate(string? name, CombatOptions options)
    {
        var failures = new List<string>();
        if (options.AilmentChanceScale < 1 || options.AilmentChanceScale > CombatOptions.MaxAilmentChanceScale)
        {
            failures.Add(
                $"{CombatOptions.SectionName}:AilmentChanceScale must be between 1 and "
                + $"{CombatOptions.MaxAilmentChanceScale}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
