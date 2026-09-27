using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Makes accounts and changes passwords for the console (System Architecture §10). It calls the store directly,
///     outside the persistence writer: it touches only accounts and their session tokens, never a character row
///     (Persistence §9). Each call is bounded by <see cref="PersistenceOptions.CommandTimeoutMs" />, and the password is
///     hashed off the caller's thread.
/// </summary>
public sealed class AccountService
{
    private readonly IGameStore m_store;
    private readonly PasswordHasher m_hasher;
    private readonly TimeProvider m_time;
    private readonly AuditLog m_audit;
    private readonly int m_timeoutMs;

    public AccountService(
        IGameStore store,
        PasswordHasher hasher,
        TimeProvider time,
        IOptions<PersistenceOptions> persistence,
        AuditLog audit)
    {
        m_store = store;
        m_hasher = hasher;
        m_time = time;
        m_audit = audit;
        m_timeoutMs = persistence.Value.CommandTimeoutMs;
    }

    public Task<AccountCommandResult> CreateAsync(AdminActor actor, string login, string password)
    {
        return Validate(login, password, out string normalized)
            ?? Task.Run(() => CreateCoreAsync(actor, normalized, password));
    }

    /// <summary>
    ///     Replaces the account's password; its session tokens end with it.
    /// </summary>
    public Task<AccountCommandResult> SetPasswordAsync(AdminActor actor, string login, string password)
    {
        return Validate(login, password, out string normalized)
            ?? Task.Run(() => SetPasswordCoreAsync(actor, normalized, password));
    }

    private static Task<AccountCommandResult>? Validate(string login, string password, out string normalized)
    {
        if (!AccountCredentialRules.TryNormalizeLogin(login, out normalized))
        {
            return Task.FromResult(new AccountCommandResult(AccountCommandOutcome.InvalidLogin));
        }

        return AccountCredentialRules.IsValidPassword(password)
            ? null
            : Task.FromResult(new AccountCommandResult(AccountCommandOutcome.InvalidPassword));
    }

    private async Task<AccountCommandResult> CreateCoreAsync(AdminActor actor, string login, string password)
    {
        string hash = m_hasher.Hash(password);
        DateTime now = m_time.GetUtcNow().UtcDateTime;
        AccountId? account;
        using (var timeout = new CancellationTokenSource(m_timeoutMs))
        {
            try
            {
                account = await m_store.CreateAccountAsync(login, PasswordHasher.Scheme, hash, now, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (StoreUnavailableException)
            {
                return new AccountCommandResult(AccountCommandOutcome.Unavailable);
            }
        }

        if (account is not AccountId created)
        {
            return new AccountCommandResult(AccountCommandOutcome.LoginTaken);
        }

        m_audit.OperatorAccountCreated(actor, created);
        return new AccountCommandResult(AccountCommandOutcome.Created, created);
    }

    private async Task<AccountCommandResult> SetPasswordCoreAsync(AdminActor actor, string login, string password)
    {
        string hash = m_hasher.Hash(password);
        AccountId? account;
        using (var timeout = new CancellationTokenSource(m_timeoutMs))
        {
            try
            {
                account = await m_store.SetAccountPasswordAsync(login, PasswordHasher.Scheme, hash, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (StoreUnavailableException)
            {
                return new AccountCommandResult(AccountCommandOutcome.Unavailable);
            }
        }

        if (account is not AccountId changed)
        {
            return new AccountCommandResult(AccountCommandOutcome.NoSuchAccount);
        }

        m_audit.OperatorPasswordChanged(actor, changed);
        return new AccountCommandResult(AccountCommandOutcome.PasswordChanged, changed);
    }
}
}
