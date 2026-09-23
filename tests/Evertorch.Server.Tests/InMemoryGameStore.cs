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
    public IReadOnlyList<string> PendingMigrations { get; set; } = Array.Empty<string>();

    public bool IsUnavailable { get; set; }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult(PendingMigrations);
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
