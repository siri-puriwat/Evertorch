using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
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
    private static readonly Action<ILogger, string, Exception?> LogUnknownDefinitions =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4007, "UnknownPersistedDefinitions"),
            "Stored characters refer to definitions the loaded content lacks: {Definitions}. "
            + "Those characters cannot enter the world; their data is kept for a content migration.");

    private readonly PersistenceWorker m_persistence;
    private readonly IGameStore m_store;
    private readonly ServerContent m_content;
    private readonly ILogger<DatabaseStartupCheck> m_logger;

    public DatabaseStartupCheck(
        PersistenceWorker persistence,
        IGameStore store,
        ServerContent content,
        ILogger<DatabaseStartupCheck> logger)
    {
        m_persistence = persistence;
        m_store = store;
        m_content = content;
        m_logger = logger;
    }

    // The probe logs what it finds. Unreachable leaves the writer probing, and a pending migration it finds later
    // keeps the server unready (Persistence §2).
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pending = m_persistence.Probe();
        if (pending.Count > 0)
        {
            throw new PendingMigrationsException(pending);
        }

        if (m_persistence.State == DatabaseState.Available)
        {
            await ReportUnknownDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // Startup compares the definitions stored data refers to with the loaded content (Persistence §8). Nothing is
    // deleted or converted; the affected characters are refused when they try to enter.
    private async Task ReportUnknownDefinitionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> stored;
        try
        {
            stored = await m_store.ListStoredDefinitionIdsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StoreUnavailableException)
        {
            return;
        }

        var unknown = new List<string>();
        foreach (string id in stored)
        {
            bool isKnown = (JobDefinitionId.TryCreate(id, out JobDefinitionId job) && m_content.Jobs.ContainsKey(job))
                || (MapDefinitionId.TryCreate(id, out MapDefinitionId map) && m_content.Maps.ContainsKey(map))
                || (ItemDefinitionId.TryCreate(id, out ItemDefinitionId item) && m_content.Items.ContainsKey(item));
            if (!isKnown)
            {
                unknown.Add(id);
            }
        }

        if (unknown.Count > 0)
        {
            LogUnknownDefinitions(m_logger, string.Join(", ", unknown), null);
        }
    }
}
}
