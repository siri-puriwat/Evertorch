using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Evertorch.Persistence
{
/// <summary>
///     Schema operations outside a game unit of work. The game server never applies migrations (Persistence §2): it
///     only asks which are pending. Applying them is for <c>dotnet-ef</c> and for tests that need a fresh database.
/// </summary>
public static class EvertorchDatabase
{
    /// <summary>
    ///     Checks <paramref name="connectionString" /> without opening a connection.
    /// </summary>
    public static bool TryValidateConnectionString(string? connectionString, out string error)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "is empty";
            return false;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
            {
                error = "must name a host and a database";
                return false;
            }
        }
        catch (ArgumentException)
        {
            // The message may quote the offending text, and that text may be a password.
            error = "is not a valid PostgreSQL connection string";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static async Task ApplyMigrationsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var context = new EvertorchDbContext(CreateOptions(connectionString));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     The migrations this build knows that the database has not applied, oldest first.
    /// </summary>
    /// <exception cref="StoreUnavailableException">The database could not be reached.</exception>
    public static async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var context = new EvertorchDbContext(CreateOptions(connectionString));
            IEnumerable<string> pending =
                await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            return pending.ToList();
        }
        catch (Exception exception) when (StoreFailures.IsUnavailable(exception, cancellationToken))
        {
            throw new StoreUnavailableException(exception);
        }
    }

    internal static DbContextOptions<EvertorchDbContext> CreateOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<EvertorchDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}
}
