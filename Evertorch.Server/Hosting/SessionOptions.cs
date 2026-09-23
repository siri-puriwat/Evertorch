using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     How long a character outlives its connection (Network Protocol §3).
/// </summary>
public sealed class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>
    ///     How long a character whose connection was lost stays in the world, standing still, for a reconnect to attach
    ///     to. 0 removes it at once.
    /// </summary>
    public int ReconnectGraceMs { get; set; } = 30000;
}

public sealed class SessionOptionsValidator : IValidateOptions<SessionOptions>
{
    public ValidateOptionsResult Validate(string? name, SessionOptions options)
    {
        return options.ReconnectGraceMs < 0 || options.ReconnectGraceMs > 600000
            ? ValidateOptionsResult.Fail($"{SessionOptions.SectionName}:ReconnectGraceMs must be between 0 and 600000.")
            : ValidateOptionsResult.Success;
    }
}
}
