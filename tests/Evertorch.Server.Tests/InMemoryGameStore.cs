using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A test double of the durable store for simulation tests that need persistence behaviour but not a database
///     (Coding Standards §10). <see cref="PendingMigrations" /> and <see cref="IsUnavailable" /> script its answers.
/// </summary>
internal sealed class InMemoryGameStore : IGameStore
{
    private readonly object m_gate = new();
    private readonly Dictionary<string, long> m_accounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_disabledLogins = new(StringComparer.Ordinal);
    private long m_lastAccount;

    public IReadOnlyList<string> PendingMigrations { get; set; } = Array.Empty<string>();

    public bool IsUnavailable { get; set; }

    /// <summary>
    ///     Every login provisioned, in order, including repeats.
    /// </summary>
    public List<string> Logins { get; } = new();

    public int AccountCount
    {
        get
        {
            lock (m_gate)
            {
                return m_accounts.Count;
            }
        }
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult(PendingMigrations);
    }

    public Task<AccountId?> ProvisionAccountAsync(
        string loginNormalized,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            Logins.Add(loginNormalized);
            if (!m_accounts.TryGetValue(loginNormalized, out long id))
            {
                id = ++m_lastAccount;
                m_accounts.Add(loginNormalized, id);
            }

            AccountId? account = m_disabledLogins.Contains(loginNormalized) ? null : new AccountId(id);
            return Task.FromResult(account);
        }
    }

    public void Disable(string loginNormalized)
    {
        lock (m_gate)
        {
            m_disabledLogins.Add(loginNormalized);
        }
    }

    private void ThrowIfUnavailable()
    {
        if (IsUnavailable)
        {
            throw new StoreUnavailableException(new TimeoutException("scripted outage"));
        }
    }
}
}
