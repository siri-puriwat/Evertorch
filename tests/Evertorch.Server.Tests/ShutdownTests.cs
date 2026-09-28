using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Evertorch.Client;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The shutdown order of System Architecture §13 when something goes wrong: the host's token already spent, a
///     faulted simulation, and a tick that never ends. A player in the world shows what was checkpointed and what the
///     client was told.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShutdownTests
{
    private const int CommandTimeoutMs = 300;

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private static IHost Build(TemporaryDirectory root, InMemoryGameStore store, Action<TickContext> onTick)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        HostApplicationBuilder builder = TestHosts.CreateBuilder(
            new[]
            {
                "--Network:Port=0",
                "--DevelopmentAuthentication:Enabled=true",
                $"--Persistence:CommandTimeoutMs={CommandTimeoutMs}"
            },
            root.Path,
            store);
        builder.Services.AddSingleton<ITickPhase>(
            new RecordingPhase(TickPhase.Movement, "test", new List<string>(), onTick));
        return builder.Build();
    }

    private static SocketClient EnterWorld(IHost host, string identity)
    {
        var client = new SocketClient(host.Services.GetRequiredService<ServerContent>(), identity, "Shutdown1");
        client.EnterWorld(host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort);
        return client;
    }

    private static DisconnectReason? NoticeAfterClose(SocketClient client)
    {
        bool isClosed = client.PumpUntil(() => client.Connection.State == ClientConnectionState.Disconnected);
        Assert.That(isClosed, Is.True, "the client was disconnected");
        return client.Connection.Notice?.Reason;
    }

    [Test]
    public void Dispose_WhenATickNeverEnds_ReturnsAfterItsBound()
    {
        using var root = new TemporaryDirectory();
        using var inTick = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using IHost host = Build(root, new InMemoryGameStore(), _ =>
        {
            inTick.Set();
            release.Wait();
        });
        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();
        lifetime.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(inTick.Wait(SignalTimeout), Is.True, "a tick started");

        var elapsed = Stopwatch.StartNew();
        lifetime.Dispose();
        TimeSpan disposedAfter = elapsed.Elapsed;
        bool wasRunning = lifetime.IsSimulationRunning;
        release.Set();

        Assert.That(disposedAfter, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(wasRunning, Is.True, "the tick was still running when Dispose gave up on it");
        Assert.That(SpinWait.SpinUntil(() => !lifetime.IsSimulationRunning, SignalTimeout), Is.True);
    }

    [Test]
    public void ShutdownTimeout_CoversTheJoinTheDrainAndTheTransport()
    {
        using var root = new TemporaryDirectory();
        using IHost host = Build(root, new InMemoryGameStore(), _ =>
        {
        });

        TimeSpan timeout = host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout;

        Assert.That(timeout, Is.EqualTo(TimeSpan.FromMilliseconds(2 * CommandTimeoutMs) + TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Stop_AfterASimulationFault_WritesNoCheckpointAndSaysInternalError()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using var fail = new ManualResetEventSlim();
        using IHost host = Build(root, store, _ =>
        {
            if (fail.IsSet)
            {
                throw new InvalidOperationException("Simulated fault in a tick.");
            }
        });
        host.Start();
        using SocketClient client = EnterWorld(host, "shutdown-fault");
        IHostApplicationLifetime application = host.Services.GetRequiredService<IHostApplicationLifetime>();

        fail.Set();
        bool isStopping = application.ApplicationStopping.WaitHandle.WaitOne(SignalTimeout);
        host.StopAsync().GetAwaiter().GetResult();

        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();
        Assert.That(isStopping, Is.True);
        Assert.That(lifetime.HasFaulted, Is.True);
        Assert.That(lifetime.HasFailed, Is.True, "the process exits with code 1");
        Assert.That(store.Checkpoints, Is.Empty, "the unknown world was not checkpointed");
        Assert.That(NoticeAfterClose(client), Is.EqualTo(DisconnectReason.InternalError));
    }

    [Test]
    public void Stop_WhenATickNeverEnds_GivesUpAfterItsBoundAndStillTellsTheClient()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using var hang = new ManualResetEventSlim();
        using var inTick = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using IHost host = Build(root, store, _ =>
        {
            if (hang.IsSet)
            {
                inTick.Set();
                release.Wait();
            }
        });
        host.Start();
        using SocketClient client = EnterWorld(host, "shutdown-hang");
        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();
        hang.Set();
        Assert.That(inTick.Wait(SignalTimeout), Is.True, "a tick hung");

        var elapsed = Stopwatch.StartNew();
        try
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            release.Set();
        }

        TimeSpan stoppedAfter = elapsed.Elapsed;
        Assert.That(stoppedAfter, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(lifetime.HasTimedOut, Is.True);
        Assert.That(lifetime.HasFailed, Is.True, "the process exits with code 1");
        Assert.That(store.Checkpoints, Is.Empty, "a world still changing was not checkpointed");
        Assert.That(NoticeAfterClose(client), Is.EqualTo(DisconnectReason.InternalError));
        Assert.That(SpinWait.SpinUntil(() => !lifetime.IsSimulationRunning, SignalTimeout), Is.True);
    }

    [Test]
    public void Stop_WithTheHostsTokenAlreadyCancelled_StillCheckpointsDrainsAndSaysMaintenance()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using IHost host = Build(root, store, _ =>
        {
        });
        host.Start();
        using SocketClient client = EnterWorld(host, "shutdown-token");
        long character = client.Connection.Characters.Single().Character.Value;

        host.StopAsync(new CancellationToken(true)).GetAwaiter().GetResult();

        ServerLifetimeService lifetime = host.Services.GetRequiredService<ServerLifetimeService>();
        Assert.That(lifetime.HasFailed, Is.False);
        Assert.That(store.Checkpoints.Select(checkpoint => checkpoint.CharacterId), Does.Contain(character));
        Assert.That(NoticeAfterClose(client), Is.EqualTo(DisconnectReason.Maintenance));
    }
}
}
