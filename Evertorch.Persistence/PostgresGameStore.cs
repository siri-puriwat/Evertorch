using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Evertorch.Persistence
{
/// <summary>
///     <see cref="IGameStore" /> on PostgreSQL through EF Core. Every call opens its own context, so the store is safe
///     to share, but the server still calls it from one writer at a time (Persistence §9).
/// </summary>
public sealed class PostgresGameStore : IGameStore
{
    private readonly string m_connectionString;
    private readonly DbContextOptions<EvertorchDbContext> m_options;

    public PostgresGameStore(string connectionString)
    {
        if (!EvertorchDatabase.TryValidateConnectionString(connectionString, out string error))
        {
            throw new ArgumentException($"The connection string {error}.", nameof(connectionString));
        }

        m_connectionString = connectionString;
        m_options = EvertorchDatabase.CreateOptions(connectionString);
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        return EvertorchDatabase.GetPendingMigrationsAsync(m_connectionString, cancellationToken);
    }

    public Task<AccountId?> ProvisionAccountAsync(
        string loginNormalized,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                // One statement, so two first logins of the same identity cannot create two accounts.
                List<AccountLogin> rows = await context.Database
                    .SqlQuery<AccountLogin>(
                        $@"INSERT INTO accounts (login_normalized, status, created_at, last_login_at)
VALUES ({loginNormalized}, {AccountStatus.Active}, {now}, {now})
ON CONFLICT (login_normalized) DO UPDATE SET last_login_at = EXCLUDED.last_login_at
RETURNING id AS ""Id"", status AS ""Status""")
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                AccountLogin login = rows.Single();
                return login.Status == AccountStatus.Active ? new AccountId(login.Id) : (AccountId?)null;
            },
            cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<EvertorchDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = new EvertorchDbContext(m_options);
            return await work(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (StoreFailures.IsUnavailable(exception, cancellationToken))
        {
            throw new StoreUnavailableException(exception);
        }
    }

    private sealed class AccountLogin
    {
        public long Id { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
}
