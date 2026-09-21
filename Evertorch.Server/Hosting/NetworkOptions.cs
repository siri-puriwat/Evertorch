using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>
    /// How long a connection may stay silent before its hello. Converted to ticks at the configured rate.
    /// </summary>
    public int HandshakeTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Decoded messages that may wait for the next tick. More than this are dropped and counted.
    /// </summary>
    public int MaxInboundEvents { get; set; } = 4096;
}

internal sealed class NetworkOptionsValidator : IValidateOptions<NetworkOptions>
{
    public ValidateOptionsResult Validate(string? name, NetworkOptions options)
    {
        List<string> failures = new List<string>();
        if (options.HandshakeTimeoutMs < 100 || options.HandshakeTimeoutMs > 60000)
        {
            failures.Add(NetworkOptions.SectionName + ":HandshakeTimeoutMs must be between 100 and 60000.");
        }

        if (options.MaxInboundEvents < 16 || options.MaxInboundEvents > 1000000)
        {
            failures.Add(NetworkOptions.SectionName + ":MaxInboundEvents must be between 16 and 1000000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
