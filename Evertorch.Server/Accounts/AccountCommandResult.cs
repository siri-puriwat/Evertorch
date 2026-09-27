using Evertorch.Persistence;

namespace Evertorch.Server
{
public enum AccountCommandOutcome
{
    Created,
    PasswordChanged,
    InvalidLogin,
    InvalidPassword,
    LoginTaken,
    NoSuchAccount,
    Unavailable
}

/// <summary>
///     The answer to a console account command: never the login or the password, only the account's number.
/// </summary>
public sealed class AccountCommandResult
{
    public AccountCommandResult(AccountCommandOutcome outcome, AccountId? account = null)
    {
        Outcome = outcome;
        Account = account;
    }

    public AccountCommandOutcome Outcome { get; }

    public AccountId? Account { get; }
}
}
