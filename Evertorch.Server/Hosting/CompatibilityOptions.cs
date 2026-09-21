using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class CompatibilityOptions
{
    public const string SectionName = "Compatibility";
    public const string DefaultBuildVersion = "0.2.0-dev";

    /// <summary>
    /// Reported in the handshake. A client with exactly this build version is always admitted.
    /// </summary>
    public string ServerBuildVersion { get; set; } = DefaultBuildVersion;

    /// <summary>
    /// Other client build versions this server admits, compared exactly. Empty by default, because configuration
    /// binding appends to a list and would otherwise never be able to remove a built-in entry.
    /// </summary>
    public List<string> AdditionalClientBuildVersions { get; set; } = new List<string>();

    public bool Accepts(string clientBuildVersion)
    {
        return string.Equals(clientBuildVersion, ServerBuildVersion, StringComparison.Ordinal)
               || AdditionalClientBuildVersions.Contains(clientBuildVersion);
    }
}

internal sealed class CompatibilityOptionsValidator : IValidateOptions<CompatibilityOptions>
{
    public ValidateOptionsResult Validate(string? name, CompatibilityOptions options)
    {
        List<string> failures = new List<string>();
        if (!IsSendable(options.ServerBuildVersion))
        {
            failures.Add(
                CompatibilityOptions.SectionName + ":ServerBuildVersion must be 1 to "
                                                 + ProtocolLimits.MaxBuildVersionBytes + " bytes of UTF-8.");
        }

        if (options.AdditionalClientBuildVersions == null
            || !options.AdditionalClientBuildVersions.TrueForAll(IsSendable))
        {
            failures.Add(
                CompatibilityOptions.SectionName + ":AdditionalClientBuildVersions entries must be 1 to "
                                                 + ProtocolLimits.MaxBuildVersionBytes + " bytes of UTF-8.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSendable(string? value)
    {
        return !string.IsNullOrEmpty(value)
               && Encoding.UTF8.GetByteCount(value) <= ProtocolLimits.MaxBuildVersionBytes;
    }
}
}
