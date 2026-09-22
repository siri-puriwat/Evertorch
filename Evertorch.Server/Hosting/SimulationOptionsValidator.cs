using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class SimulationOptionsValidator : IValidateOptions<SimulationOptions>
{
    public ValidateOptionsResult Validate(string? name, SimulationOptions options)
    {
        var failures = new List<string>();

        if (options.TickRate < SimulationOptions.MinimumTickRate
            || options.TickRate > SimulationOptions.MaximumTickRate)
        {
            failures.Add(
                RangeFailure("TickRate", SimulationOptions.MinimumTickRate, SimulationOptions.MaximumTickRate));
        }

        if (options.MaxCatchUpTicks < 1 || options.MaxCatchUpTicks > SimulationOptions.MaximumMaxCatchUpTicks)
        {
            failures.Add(RangeFailure("MaxCatchUpTicks", 1, SimulationOptions.MaximumMaxCatchUpTicks));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string RangeFailure(string key, int minimum, int maximum)
    {
        return SimulationOptions.SectionName + ":" + key + " must be between " + minimum + " and " + maximum + ".";
    }
}
}
