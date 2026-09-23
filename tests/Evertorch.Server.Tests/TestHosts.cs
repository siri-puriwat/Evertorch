using System.Linq;
using Evertorch.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The real server composition for tests that do not exercise the database: a valid connection string that no
///     test ever opens, and an in-memory store in place of PostgreSQL.
/// </summary>
internal static class TestHosts
{
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=evertorch;Username=evertorch;Password=unused;Timeout=1";

    public static HostApplicationBuilder CreateBuilder(string[] args, string contentRootPath)
    {
        return CreateBuilder(args, contentRootPath, new InMemoryGameStore());
    }

    public static HostApplicationBuilder CreateBuilder(string[] args, string contentRootPath, IGameStore store)
    {
        HostApplicationBuilder builder =
            ServerHost.CreateBuilder(WithDatabase(args, UnreachableDatabase), contentRootPath);
        builder.Services.AddSingleton(store);
        return builder;
    }

    /// <summary>
    ///     The server composition against a real database, with nothing replaced.
    /// </summary>
    public static HostApplicationBuilder CreateBuilderWithDatabase(
        string[] args,
        string contentRootPath,
        string connectionString)
    {
        return ServerHost.CreateBuilder(WithDatabase(args, connectionString), contentRootPath);
    }

    private static string[] WithDatabase(string[] args, string connectionString)
    {
        return args.Append($"--ConnectionStrings:{DatabaseOptions.ConnectionName}={connectionString}").ToArray();
    }
}
}
