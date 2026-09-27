namespace Evertorch.Persistence
{
/// <summary>
///     What a sign-in checks of an account: its stored password, null for a development account, and its status.
/// </summary>
public sealed class AccountCredentials
{
    public AccountCredentials(AccountId account, string? passwordScheme, string? passwordHash, bool isDisabled)
    {
        Account = account;
        PasswordScheme = passwordScheme;
        PasswordHash = passwordHash;
        IsDisabled = isDisabled;
    }

    public AccountId Account { get; }

    public string? PasswordScheme { get; }

    public string? PasswordHash { get; }

    public bool IsDisabled { get; }
}
}
