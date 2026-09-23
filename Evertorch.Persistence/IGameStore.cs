using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Evertorch.Persistence
{
/// <summary>
///     Everything the server asks of durable storage. Each call is one unit of work with its own connection, and
///     results are plain values: no database type or tracked entity reaches the simulation.
/// </summary>
/// <remarks>
///     A call that cannot reach the database throws <see cref="StoreUnavailableException" />; retrying it later is
///     safe. Any other exception is a permanent failure of that operation.
/// </remarks>
public interface IGameStore
{
    /// <summary>
    ///     The migrations this build knows that the database has not applied, oldest first.
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);
}
}
