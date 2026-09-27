using System.Linq;
using Evertorch.Persistence;
using Evertorch.Rules;
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

    /// <summary>
    ///     The server composition against a real database with the HTTPS gateway on an ephemeral port, serving
    ///     <paramref name="certificate" />, which the test's own client pins (Coding Standards §10).
    /// </summary>
    public static HostApplicationBuilder CreateBuilderWithGateway(
        string[] args,
        string contentRootPath,
        string connectionString,
        TestCertificate certificate)
    {
        return ServerHost.CreateBuilder(WithGateway(WithDatabase(args, connectionString), certificate),
            contentRootPath);
    }

    /// <summary>
    ///     The same with an in-memory store in place of PostgreSQL.
    /// </summary>
    public static HostApplicationBuilder CreateBuilderWithGateway(
        string[] args,
        string contentRootPath,
        IGameStore store,
        TestCertificate certificate)
    {
        HostApplicationBuilder builder = ServerHost.CreateBuilder(
            WithGateway(WithDatabase(args, UnreachableDatabase), certificate),
            contentRootPath);
        builder.Services.AddSingleton(store);
        return builder;
    }

    /// <summary>
    ///     Gives combat and drops their own random sources. The host draws every outcome from one source, and over
    ///     real sockets the network's timing decides which draw each roll gets, so a seed alone cannot script a fight.
    ///     Monster placement and AI keep the server's own source.
    /// </summary>
    public static void ScriptOutcomes(HostApplicationBuilder builder, IRandomSource combat, IRandomSource drops)
    {
        builder.Services.AddSingleton(services => ActivatorUtilities.CreateInstance<ItemDropSystem>(services, drops));
        builder.Services.AddSingleton(services => ActivatorUtilities.CreateInstance<CombatSystem>(services, combat));
    }

    // Appended after the harness's own switches, so they win; the tests' hash cost goes first, so a test may raise it.
    private static string[] WithGateway(string[] args, TestCertificate certificate)
    {
        return new[] { $"--Accounts:PasswordIterations={TestServer.TestPasswordIterations}" }
            .Concat(args)
            .Append("--Gateway:Enabled=true")
            .Append("--Gateway:Port=0")
            .Append($"--Gateway:CertificatePath={certificate.Path}")
            .ToArray();
    }

    // The test output carries the server's appsettings.json, which names a fixed health port and turns the gateway
    // on at a fixed port.
    private static string[] WithDatabase(string[] args, string connectionString)
    {
        return args
            .Append($"--ConnectionStrings:{DatabaseOptions.ConnectionName}={connectionString}")
            .Append("--Health:Port=0")
            .Append("--Gateway:Enabled=false")
            .ToArray();
    }
}
}
