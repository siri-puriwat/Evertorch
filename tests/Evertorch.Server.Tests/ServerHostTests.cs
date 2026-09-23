using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
[NonParallelizable]
public sealed class ServerHostTests
{
    private const string TickRateVariable = "Simulation__TickRate";

    // A started host binds a socket; port 0 keeps tests from colliding with each other or a running server.
    private static readonly string[] EphemeralPort = { "--Network:Port=0" };

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private static void WriteValidContent(TemporaryDirectory root)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
    }

    private static SimulationOptions ReadOptions(IHost host)
    {
        return host.Services.GetRequiredService<IOptions<SimulationOptions>>().Value;
    }

    [TestCase("-0.5")]
    [TestCase("10.5")]
    public void Start_WithPickupRangeOutsideZeroToTen_FailsNamingTheKey(string range)
    {
        using var root = new TemporaryDirectory();
        root.Write("appsettings.json", $"{{ \"World\": {{ \"PickupRange\": {range} }} }}");
        WriteValidContent(root);
        using IHost host = TestHosts.CreateBuilder(EphemeralPort, root.Path).Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<OptionsValidationException>().With.Message.Contains("World:PickupRange"));
    }

    [Test]
    public void CreateBuilder_WithCommandLineOverride_WinsOverEnvironment()
    {
        using var root = new TemporaryDirectory();
        string? previousTickRate = Environment.GetEnvironmentVariable(TickRateVariable);
        Environment.SetEnvironmentVariable(TickRateVariable, "40");
        try
        {
            string[] args = { "--Simulation:TickRate=50", "--Network:Port=0" };
            using IHost host = TestHosts.CreateBuilder(args, root.Path).Build();

            Assert.That(ReadOptions(host).TickRate, Is.EqualTo(50));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickRateVariable, previousTickRate);
        }
    }

    [Test]
    public void CreateBuilder_WithEnvironmentOverride_ReplacesJsonTickRate()
    {
        using var root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 30 } }");
        string? previousTickRate = Environment.GetEnvironmentVariable(TickRateVariable);
        Environment.SetEnvironmentVariable(TickRateVariable, "40");
        try
        {
            using IHost host = TestHosts.CreateBuilder(EphemeralPort, root.Path).Build();

            Assert.That(ReadOptions(host).TickRate, Is.EqualTo(40));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickRateVariable, previousTickRate);
        }
    }

    [Test]
    public void CreateBuilder_WithJsonFile_ReadsTickRate()
    {
        using var root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 30 } }");
        using IHost host = TestHosts.CreateBuilder(EphemeralPort, root.Path).Build();

        Assert.That(ReadOptions(host).TickRate, Is.EqualTo(30));
    }

    [Test]
    public void CreateBuilder_WithoutConfigurationFiles_UsesTwentyHertz()
    {
        using var root = new TemporaryDirectory();
        using IHost host = TestHosts.CreateBuilder(EphemeralPort, root.Path).Build();

        Assert.That(ReadOptions(host).TickRate, Is.EqualTo(20));
    }

    [Test]
    public void Dispose_WithoutStop_EndsSimulationThread()
    {
        using var root = new TemporaryDirectory();
        WriteValidContent(root);
        using var ticked = new ManualResetEventSlim();
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(
            new RecordingPhase(TickPhase.Movement, "tick", new List<string>(), _ => ticked.Set()));
        IHost host = builder.Build();
        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();

        host.Start();
        bool didTick = ticked.Wait(SignalTimeout);
        host.Dispose();

        Assert.That(didTick, Is.True);
        Assert.That(lifetime.IsSimulationRunning, Is.False);
    }

    [Test]
    public void Simulation_WhenPhaseThrows_StopsApplicationAndReportsFault()
    {
        using var root = new TemporaryDirectory();
        WriteValidContent(root);
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(
            new RecordingPhase(
                TickPhase.Movement,
                "tick",
                new List<string>(),
                _ => throw new InvalidOperationException("boom")));
        using IHost host = builder.Build();
        IHostApplicationLifetime applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        host.Start();
        bool isStopping = applicationLifetime.ApplicationStopping.WaitHandle.WaitOne(SignalTimeout);
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isStopping, Is.True);
        Assert.That(host.Services.GetRequiredService<ServerLifetimeService>().HasFaulted, Is.True);
    }

    [Test]
    public void Start_WhenContentPackageIsInvalid_FailsBeforeSimulationStarts()
    {
        using var root = new TemporaryDirectory();
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.ReplaceWithoutManifest(files, "jobs.json", "\"baseSpeed\": 5", "\"baseSpeed\": 500");
        PackageFixture.WriteTo(Path.Combine(root.Path, "content", "server"), files);
        var log = new List<string>();
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(new RecordingPhase(TickPhase.Movement, "tick", log));
        using IHost host = builder.Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<ContentLoadException>().With.Message.Contains("jobs.json"));
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void Start_WhenContentPackageIsMissing_FailsBeforeSimulationStarts()
    {
        using var root = new TemporaryDirectory();
        var log = new List<string>();
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(new RecordingPhase(TickPhase.Movement, "tick", log));
        using IHost host = builder.Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<ContentLoadException>());
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void Start_WithConfiguredPackagePath_LoadsContentFromThere()
    {
        using var root = new TemporaryDirectory();
        using var package = new TemporaryDirectory();
        PackageFixture.WriteTo(package.Path, PackageFixture.BuildRepositoryPackage());
        string[] args = { $"--Content:ServerPackagePath={package.Path}", "--Network:Port=0" };
        using IHost host = TestHosts.CreateBuilder(args, root.Path).Build();

        host.Start();
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(content.Maps, Is.Not.Empty);
    }

    [Test]
    public void Start_WithInvalidTickRate_FailsBeforeSimulationStarts()
    {
        using var root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 0 } }");
        WriteValidContent(root);
        var log = new List<string>();
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(new RecordingPhase(TickPhase.Movement, "tick", log));
        using IHost host = builder.Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<OptionsValidationException>());
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void Stop_WhileSimulationRuns_JoinsSimulationThread()
    {
        using var root = new TemporaryDirectory();
        WriteValidContent(root);
        using var ticked = new ManualResetEventSlim();
        HostApplicationBuilder builder = TestHosts.CreateBuilder(EphemeralPort, root.Path);
        builder.Services.AddSingleton<ITickPhase>(
            new RecordingPhase(TickPhase.Movement, "tick", new List<string>(), _ => ticked.Set()));
        using IHost host = builder.Build();
        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();

        host.Start();
        bool didTick = ticked.Wait(SignalTimeout);
        bool wasRunning = lifetime.IsSimulationRunning;
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(didTick, Is.True);
        Assert.That(wasRunning, Is.True);
        Assert.That(lifetime.IsSimulationRunning, Is.False);
        Assert.That(lifetime.HasFaulted, Is.False);
    }
}
}
