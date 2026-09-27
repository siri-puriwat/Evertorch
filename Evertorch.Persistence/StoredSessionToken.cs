using System;

namespace Evertorch.Persistence
{
/// <summary>
///     A stored session token, with the status of its account, for a hello's check (Network Protocol §4).
/// </summary>
public sealed class StoredSessionToken
{
    public StoredSessionToken(AccountId account, DateTime expiresAt, bool isAccountDisabled)
    {
        Account = account;
        ExpiresAt = expiresAt;
        IsAccountDisabled = isAccountDisabled;
    }

    public AccountId Account { get; }

    public DateTime ExpiresAt { get; }

    public bool IsAccountDisabled { get; }
}
}
