using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The health endpoints (System Architecture §10, §12). Off in code and on in <c>appsettings.json</c>, which also
///     names the port, so a host built without that file serves them only when asked to.
/// </summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    public bool Enabled { get; set; }

    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    ///     The TCP port; 0 lets the operating system choose one.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    ///     Age of the published status beyond which the simulation counts as stalled. The status is published once a
    ///     second.
    /// </summary>
    public int LivenessStallMs { get; set; } = 5000;
}

public sealed class HealthOptionsValidator : IValidateOptions<HealthOptions>
{
    public ValidateOptionsResult Validate(string? name, HealthOptions options)
    {
        var failures = new List<string>();
        if (!IPAddress.TryParse(options.BindAddress, out IPAddress? _))
        {
            failures.Add($"{HealthOptions.SectionName}:BindAddress must be an IP address.");
        }

        if (options.Port < 0 || options.Port > 65535)
        {
            failures.Add($"{HealthOptions.SectionName}:Port must be between 0 and 65535.");
        }

        if (options.LivenessStallMs < 2000 || options.LivenessStallMs > 600000)
        {
            failures.Add($"{HealthOptions.SectionName}:LivenessStallMs must be between 2000 and 600000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
