using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The market board's periods (Gameplay Systems §11.5; System Architecture §12): how much listed time makes a price
///     fall a step, which is also the least time between two rises, and how long a listing sells. Tests and the owner's
///     walk shorten them.
/// </summary>
public sealed class MarketOptions
{
    public const string SectionName = "Market";

    public const int MinPeriodMs = 1000;
    public const int MaxPricePeriodMs = 604_800_000;
    public const int MaxListingDurationMs = 1_209_600_000;

    public int PricePeriodMs { get; set; } = 86_400_000;

    public int ListingDurationMs { get; set; } = 259_200_000;
}

public sealed class MarketOptionsValidator : IValidateOptions<MarketOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketOptions options)
    {
        var failures = new List<string>();
        if (options.PricePeriodMs < MarketOptions.MinPeriodMs || options.PricePeriodMs > MarketOptions.MaxPricePeriodMs)
        {
            failures.Add(
                $"{MarketOptions.SectionName}:PricePeriodMs must be between {MarketOptions.MinPeriodMs} and "
                + $"{MarketOptions.MaxPricePeriodMs}.");
        }

        if (options.ListingDurationMs < MarketOptions.MinPeriodMs
            || options.ListingDurationMs > MarketOptions.MaxListingDurationMs)
        {
            failures.Add(
                $"{MarketOptions.SectionName}:ListingDurationMs must be between {MarketOptions.MinPeriodMs} and "
                + $"{MarketOptions.MaxListingDurationMs}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
