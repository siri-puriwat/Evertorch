using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace Evertorch.Persistence.Tests
{
/// <summary>
///     A throwaway PostgreSQL 18 in Docker with every migration applied from zero (Persistence §11). One per test
///     fixture; it never touches the Compose development database. Without Docker the test fails and says so: these
///     tests are never skipped.
/// </summary>
/// <remarks>
///     Also compiled into <c>Evertorch.Server.Tests</c> as linked source, so the server's database tests use the same
///     container setup (Coding Standards §10).
/// </remarks>
internal sealed class PostgresFixture : IDisposable, IAsyncDisposable
{
    public const string Image = "postgres:18";

    private readonly PostgreSqlContainer m_container;

    private PostgresFixture(PostgreSqlContainer container)
    {
        m_container = container;
    }

    public string ConnectionString => m_container.GetConnectionString();

    public async ValueTask DisposeAsync()
    {
        await m_container.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="StartAsync" />
    public static PostgresFixture Start(bool applyMigrations = true)
    {
        return StartAsync(applyMigrations).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Starts the container and, unless <paramref name="applyMigrations" /> is false, brings its schema up to date.
    /// </summary>
    public static async Task<PostgresFixture> StartAsync(bool applyMigrations = true)
    {
        PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();
        try
        {
            await container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await container.DisposeAsync().ConfigureAwait(false);
            Assert.Fail(
                $"Database tests need Docker running and able to start '{Image}' (Persistence §11). "
                + $"Start Docker Desktop and run the tests again. {exception.GetType().Name}: {exception.Message}");
            throw;
        }

        var fixture = new PostgresFixture(container);
        if (!applyMigrations)
        {
            return fixture;
        }

        await EvertorchDatabase.ApplyMigrationsAsync(fixture.ConnectionString, CancellationToken.None)
            .ConfigureAwait(false);
        return fixture;
    }

    /// <summary>
    ///     Freezes the database process, so connections neither succeed nor fail until <see cref="Resume" />: an
    ///     outage as the server sees one.
    /// </summary>
    public void Pause()
    {
        m_container.PauseAsync().GetAwaiter().GetResult();
    }

    public void Resume()
    {
        m_container.UnpauseAsync().GetAwaiter().GetResult();
    }
}
}
