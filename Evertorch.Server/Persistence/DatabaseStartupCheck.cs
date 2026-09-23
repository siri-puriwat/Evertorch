using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     The first hosted service: compares the database's applied migrations with this build's before the simulation
///     starts (Persistence §2, System Architecture §13). A pending migration stops startup; an unreachable database is
///     an outage, not a startup failure.
/// </summary>
public sealed class DatabaseStartupCheck : IHostedService
{
    private const int TimeoutMs = 5000;

    private static readonly Action<ILogger, int, string, Exception?> LogMigrationsPending =
        LoggerMessage.Define<int, string>(
            LogLevel.Critical,
            new EventId(4001, "MigrationsPending"),
            "The database has {Count} pending migration(s): {Migrations}. Apply them with scripts/db-migrate.ps1.");

    private static readonly Action<ILogger, Exception?> LogDatabaseUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4002, "DatabaseUnavailable"),
            "The database cannot be reached; the server starts without it.");

    private static readonly Action<ILogger, Exception?> LogDatabaseReady =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(4003, "DatabaseReady"),
            "The database is reachable and its schema is current.");

    private readonly IGameStore m_store;
    private readonly ILogger<DatabaseStartupCheck> m_logger;

    public DatabaseStartupCheck(IGameStore store, ILogger<DatabaseStartupCheck> logger)
    {
        m_store = store;
        m_logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);
        IReadOnlyList<string> pending;
        try
        {
            pending = await m_store.GetPendingMigrationsAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (StoreUnavailableException exception)
        {
            LogDatabaseUnavailable(m_logger, exception.InnerException);
            return;
        }

        if (pending.Count > 0)
        {
            LogMigrationsPending(m_logger, pending.Count, string.Join(", ", pending), null);
            throw new PendingMigrationsException(pending);
        }

        LogDatabaseReady(m_logger, null);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
}
