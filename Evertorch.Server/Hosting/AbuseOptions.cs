using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Limits on what one peer, one address, or one session may send (Network Protocol §11). Generous by default: an
///     honest client never meets them, and many phones may share one address behind carrier NAT.
/// </summary>
public sealed class AbuseOptions
{
    public const string SectionName = "Abuse";

    /// <summary>
    ///     Switches every limit off, for the tests of the guarantees that hold without them.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Messages each peer may send per tick, on average. At least 3, so an honest client stays inside it at any
    ///     tick rate.
    /// </summary>
    public int PeerMessagesPerTick { get; set; } = 5;

    /// <summary>
    ///     Messages a peer may send at once before the per-tick budget applies.
    /// </summary>
    public int PeerMessageBurst { get; set; } = 200;

    public int ConnectionRequestsPerSecond { get; set; } = 20;

    public int MaxConnectionsPerAddress { get; set; } = 64;

    /// <summary>
    ///     Bound of the table of remote addresses the connection limits remember.
    /// </summary>
    public int MaxTrackedAddresses { get; set; } = 10000;
}

internal sealed class AbuseOptionsValidator : IValidateOptions<AbuseOptions>
{
    public ValidateOptionsResult Validate(string? name, AbuseOptions options)
    {
        var failures = new List<string>();
        AddRangeFailure(failures, "PeerMessagesPerTick", options.PeerMessagesPerTick, 3, 1000);
        AddRangeFailure(failures, "PeerMessageBurst", options.PeerMessageBurst, 1, 100000);
        AddRangeFailure(failures, "ConnectionRequestsPerSecond", options.ConnectionRequestsPerSecond, 1, 10000);
        AddRangeFailure(failures, "MaxConnectionsPerAddress", options.MaxConnectionsPerAddress, 1, 10000);
        AddRangeFailure(failures, "MaxTrackedAddresses", options.MaxTrackedAddresses, 16, 1000000);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(List<string> failures, string key, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{AbuseOptions.SectionName}:{key} must be between {minimum} and {maximum}.");
        }
    }
}
}
