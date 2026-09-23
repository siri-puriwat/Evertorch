using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Evertorch.Persistence
{
/// <summary>
///     <see cref="IGameStore" /> on PostgreSQL through EF Core. Every call opens its own context, so the store is safe
///     to share, but the server still calls it from one writer at a time (Persistence §9).
/// </summary>
public sealed class PostgresGameStore : IGameStore
{
    private readonly string m_connectionString;

    public PostgresGameStore(string connectionString)
    {
        if (!EvertorchDatabase.TryValidateConnectionString(connectionString, out string error))
        {
            throw new ArgumentException($"The connection string {error}.", nameof(connectionString));
        }

        m_connectionString = connectionString;
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        return EvertorchDatabase.GetPendingMigrationsAsync(m_connectionString, cancellationToken);
    }
}
}
