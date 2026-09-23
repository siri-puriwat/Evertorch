using System;
using System.Collections.Generic;
using System.IO;
using Evertorch.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The migration check at startup (Persistence §2, System Architecture §13), against real PostgreSQL 18.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DatabaseStartupTests
{
    private static readonly string[] EphemeralPort = { "--Network:Port=0" };

    private static TemporaryDirectory WithContent()
    {
        var root = new TemporaryDirectory();
        PackageFixture.WriteTo(Path.Combine(root.Path, "content", "server"), PackageFixture.BuildRepositoryPackage());
        return root;
    }

    private static void StartAndStop(IHost host)
    {
        host.Start();
        host.StopAsync().GetAwaiter().GetResult();
    }

    [TestCase("")]
    [TestCase("not a connection string")]
    [TestCase("Host=127.0.0.1")]
    public void Start_WithoutAUsableConnectionString_FailsOptionsValidation(string connectionString)
    {
        using TemporaryDirectory root = WithContent();
        using IHost host = ServerHost.CreateBuilder(
                new[] { "--Network:Port=0", $"--ConnectionStrings:Evertorch={connectionString}" },
                root.Path)
            .Build();
        Action start = () => StartAndStop(host);

        Assert.That(
            start,
            Throws.InstanceOf<OptionsValidationException>().With.Message.Contains("ConnectionStrings:Evertorch"));
    }

    [Test]
    public void Start_WithACurrentSchema_RunsTheSimulation()
    {
        using var database = PostgresFixture.Start();
        using TemporaryDirectory root = WithContent();
        using IHost host = TestHosts.CreateBuilderWithDatabase(EphemeralPort, root.Path, database.ConnectionString)
            .Build();

        host.Start();
        bool isRunning = host.Services.GetRequiredService<ServerLifetimeService>().IsSimulationRunning;
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isRunning, Is.True);
    }

    [Test]
    public void Start_WithAPasswordInABadConnectionString_NeverRepeatsIt()
    {
        using TemporaryDirectory root = WithContent();
        using IHost host = ServerHost.CreateBuilder(
                new[] { "--Network:Port=0", "--ConnectionStrings:Evertorch=Password=hunter2;Bogus Key=1" },
                root.Path)
            .Build();
        Action start = () => StartAndStop(host);

        Assert.That(start, Throws.InstanceOf<OptionsValidationException>().With.Message.Not.Contains("hunter2"));
    }

    [Test]
    public void Start_WithAnUnreachableDatabase_StillRunsTheSimulation()
    {
        using TemporaryDirectory root = WithContent();
        using IHost host = TestHosts
            .CreateBuilderWithDatabase(EphemeralPort, root.Path, TestHosts.UnreachableDatabase)
            .Build();

        host.Start();
        bool isRunning = host.Services.GetRequiredService<ServerLifetimeService>().IsSimulationRunning;
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isRunning, Is.True);
    }

    [Test]
    public void Start_WithPendingMigrations_FailsBeforeTheSimulationStarts()
    {
        using var database = PostgresFixture.Start(false);
        using TemporaryDirectory root = WithContent();
        var log = new List<string>();
        HostApplicationBuilder builder =
            TestHosts.CreateBuilderWithDatabase(EphemeralPort, root.Path, database.ConnectionString);
        builder.Services.AddSingleton<ITickPhase>(new RecordingPhase(TickPhase.Movement, "tick", log));
        using IHost host = builder.Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<PendingMigrationsException>().With.Message.Contains("InitialSchema"));
        Assert.That(host.Services.GetRequiredService<ServerLifetimeService>().IsSimulationRunning, Is.False);
        Assert.That(log, Is.Empty);
    }
}
}
