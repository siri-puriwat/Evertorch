using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     How long a character outlives its connection (Network Protocol §3), and how long a session token serves hellos
///     (Network Protocol §4).
/// </summary>
public sealed class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>
    ///     How long a character whose connection was lost stays in the world, standing still, for a reconnect to attach
    ///     to. 0 removes it at once.
    /// </summary>
    public int ReconnectGraceMs { get; set; } = 30000;

    public int TokenLifetimeMs { get; set; } = 900000;
}

public sealed class SessionOptionsValidator : IValidateOptions<SessionOptions>
{
    public ValidateOptionsResult Validate(string? name, SessionOptions options)
    {
        var failures = new List<string>();
        if (options.ReconnectGraceMs < 0 || options.ReconnectGraceMs > 600000)
        {
            failures.Add($"{SessionOptions.SectionName}:ReconnectGraceMs must be between 0 and 600000.");
        }

        if (options.TokenLifetimeMs < 60000 || options.TokenLifetimeMs > 86400000)
        {
            failures.Add($"{SessionOptions.SectionName}:TokenLifetimeMs must be between 60000 and 86400000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
