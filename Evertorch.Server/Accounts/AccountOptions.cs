using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     How passwords are hashed (System Architecture §12).
/// </summary>
public sealed class AccountOptions
{
    public const string SectionName = "Accounts";
    public const int MinPasswordIterations = 1000;
    public const int MaxPasswordIterations = 10_000_000;

    /// <summary>
    ///     PBKDF2 iterations of each new hash; a stored hash keeps the count it was made with.
    /// </summary>
    public int PasswordIterations { get; set; } = 600_000;
}

public sealed class AccountOptionsValidator : IValidateOptions<AccountOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountOptions options)
    {
        return options.PasswordIterations < AccountOptions.MinPasswordIterations
            || options.PasswordIterations > AccountOptions.MaxPasswordIterations
                ? ValidateOptionsResult.Fail(
                    $"{AccountOptions.SectionName}:PasswordIterations must be between "
                    + $"{AccountOptions.MinPasswordIterations} and {AccountOptions.MaxPasswordIterations}.")
                : ValidateOptionsResult.Success;
    }
}
}
