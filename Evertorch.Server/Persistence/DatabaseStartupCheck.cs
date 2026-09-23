using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server
{
/// <summary>
///     The first hosted service: compares the database's applied migrations with this build's before the simulation
///     starts (Persistence §2, System Architecture §13). A pending migration stops startup; an unreachable database is
///     an outage, not a startup failure.
/// </summary>
public sealed class DatabaseStartupCheck : IHostedService
{
    private readonly PersistenceWorker m_persistence;

    public DatabaseStartupCheck(PersistenceWorker persistence)
    {
        m_persistence = persistence;
    }

    // The probe logs what it finds. Unreachable leaves the writer probing, and a pending migration it finds later
    // keeps the server unready (Persistence §2).
    public Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pending = m_persistence.Probe();
        if (pending.Count > 0)
        {
            throw new PendingMigrationsException(pending);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
}
