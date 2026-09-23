using Evertorch.Persistence;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The database connection, read from the standard <c>ConnectionStrings:Evertorch</c> key. It is supplied by an
///     environment variable or the launcher and is never committed or logged.
/// </summary>
public sealed class DatabaseOptions
{
    public const string ConnectionName = "Evertorch";

    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        // The error never repeats the value: a connection string carries a password.
        return EvertorchDatabase.TryValidateConnectionString(options.ConnectionString, out string error)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"ConnectionStrings:{DatabaseOptions.ConnectionName} {error}. Set it with the environment variable "
                + $"ConnectionStrings__{DatabaseOptions.ConnectionName}, or start the server with scripts\\run-server.cmd.");
    }
}
}
