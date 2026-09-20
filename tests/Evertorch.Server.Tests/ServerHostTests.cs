using System;
using System.Collections.Generic;
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

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public void CreateBuilder_WithoutConfigurationFiles_UsesTwentyHertz()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        using IHost host = ServerHost.CreateBuilder(new string[0], root.Path).Build();

        Assert.That(ReadOptions(host).TickRate, Is.EqualTo(20));
    }

    [Test]
    public void CreateBuilder_WithJsonFile_ReadsTickRate()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 30 } }");
        using IHost host = ServerHost.CreateBuilder(new string[0], root.Path).Build();

        Assert.That(ReadOptions(host).TickRate, Is.EqualTo(30));
    }

    [Test]
    public void CreateBuilder_WithEnvironmentOverride_ReplacesJsonTickRate()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 30 } }");
        Environment.SetEnvironmentVariable(TickRateVariable, "40");
        try
        {
            using IHost host = ServerHost.CreateBuilder(new string[0], root.Path).Build();

            Assert.That(ReadOptions(host).TickRate, Is.EqualTo(40));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickRateVariable, null);
        }
    }

    [Test]
    public void CreateBuilder_WithCommandLineOverride_WinsOverEnvironment()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        Environment.SetEnvironmentVariable(TickRateVariable, "40");
        try
        {
            string[] args = { "--Simulation:TickRate=50" };
            using IHost host = ServerHost.CreateBuilder(args, root.Path).Build();

            Assert.That(ReadOptions(host).TickRate, Is.EqualTo(50));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickRateVariable, null);
        }
    }

    [Test]
    public void Start_WithInvalidTickRate_FailsBeforeSimulationStarts()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        root.Write("appsettings.json", "{ \"Simulation\": { \"TickRate\": 0 } }");
        List<string> log = new List<string>();
        HostApplicationBuilder builder = ServerHost.CreateBuilder(new string[0], root.Path);
        builder.Services.AddSingleton<ITickPhase>(new RecordingPhase(TickPhase.Movement, "tick", log));
        using IHost host = builder.Build();
        Action start = () => host.Start();

        Assert.That(start, Throws.InstanceOf<OptionsValidationException>());
        Assert.That(log, Is.Empty);
    }

    [Test]
    public void Stop_WhileSimulationRuns_JoinsSimulationThread()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        using ManualResetEventSlim ticked = new ManualResetEventSlim();
        HostApplicationBuilder builder = ServerHost.CreateBuilder(new string[0], root.Path);
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

    [Test]
    public void Simulation_WhenPhaseThrows_StopsApplicationAndReportsFault()
    {
        using TemporaryDirectory root = new TemporaryDirectory();
        HostApplicationBuilder builder = ServerHost.CreateBuilder(new string[0], root.Path);
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

    private static SimulationOptions ReadOptions(IHost host)
    {
        return host.Services.GetRequiredService<IOptions<SimulationOptions>>().Value;
    }
}
}
