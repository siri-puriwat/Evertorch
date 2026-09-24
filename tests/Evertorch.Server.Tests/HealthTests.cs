using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The health endpoints (System Architecture §10): the rules on a snapshot, then the endpoints of a running host.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class HealthTests
{
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    // A healthy server unless told otherwise; the database's availability follows the writer's own rule.
    private static HealthSnapshot Snapshot(
        bool hasFaulted = false,
        bool isStopping = false,
        double? statusAgeSeconds = 0.4,
        bool isAdmissionOpen = true,
        DatabaseState database = DatabaseState.Available,
        int pendingJobs = 0)
    {
        return new HealthSnapshot
        {
            HasFaulted = hasFaulted,
            IsStopping = isStopping,
            StatusAge = statusAgeSeconds.HasValue ? TimeSpan.FromSeconds(statusAgeSeconds.Value) : null,
            IsAdmissionOpen = isAdmissionOpen,
            Database = database,
            IsDatabaseAvailable = database == DatabaseState.Available || database == DatabaseState.Unknown,
            PendingJobs = pendingJobs,
            QueueCapacity = 256
        };
    }

    private static IHost StartHost(TemporaryDirectory root, InMemoryGameStore store, Action<TickContext>? onTick = null)
    {
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        HostApplicationBuilder builder = TestHosts.CreateBuilder(
            new[] { "--Network:Port=0", "--Health:Enabled=true", "--Persistence:IdleProbeIntervalMs=100" },
            root.Path,
            store);
        if (onTick != null)
        {
            builder.Services.AddSingleton<ITickPhase>(
                new RecordingPhase(TickPhase.Movement, "test", new List<string>(), onTick));
        }

        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private static HttpClient ClientFor(IHost host)
    {
        int port = host.Services.GetRequiredService<HealthEndpoint>().Port;
        return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    private static (HttpStatusCode Status, string Body) Get(HttpClient http, string path)
    {
        using HttpResponseMessage response = http.GetAsync(path).GetAwaiter().GetResult();
        return (response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static bool WaitFor(HttpClient http, string path, HttpStatusCode status, string fragment)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < WaitLimit)
        {
            (HttpStatusCode answer, string body) = Get(http, path);
            if (answer == status && body.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    [TestCase(204, false)]
    [TestCase(205, true)]
    public void Evaluate_WithTheQueueNearCapacity_IsDegradedButStillReady(int pendingJobs, bool isDegraded)
    {
        var report = HealthReport.Evaluate(Snapshot(pendingJobs: pendingJobs), StallLimit);

        Assert.That(report.IsQueueDegraded, Is.EqualTo(isDegraded));
        Assert.That(report.IsReady, Is.True);
    }

    [Test]
    public void Endpoints_OfARunningServer_AnswerLiveAndReadyInJson()
    {
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(root, new InMemoryGameStore());
        using HttpClient http = ClientFor(host);

        bool isReady = WaitFor(http, "/health/ready", HttpStatusCode.OK, "\"simulation\":\"ticking\"");
        (HttpStatusCode liveStatus, string liveBody) = Get(http, "/health/live");
        (HttpStatusCode unknownStatus, string _) = Get(http, "/health");
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isReady, Is.True);
        Assert.That(liveStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(
            liveBody,
            Is.EqualTo(
                "{\"live\":true,\"ready\":true,\"checks\":{\"simulation\":\"ticking\",\"admission\":\"open\","
                + "\"database\":\"available\",\"persistenceQueue\":\"ok\"}}"));
        Assert.That(unknownStatus, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public void Evaluate_ATickingServerWithAdmissionAndItsDatabase_IsLiveAndReady()
    {
        var report = HealthReport.Evaluate(Snapshot(), StallLimit);

        Assert.That(report.Simulation, Is.EqualTo("ticking"));
        Assert.That(report.IsLive, Is.True);
        Assert.That(report.IsReady, Is.True);
        Assert.That(report.IsQueueDegraded, Is.False);
    }

    [Test]
    public void Evaluate_AfterAFaultOrAStall_IsNeitherLiveNorReady()
    {
        var faulted = HealthReport.Evaluate(Snapshot(true), StallLimit);
        var stalled = HealthReport.Evaluate(Snapshot(statusAgeSeconds: 6), StallLimit);

        Assert.That(faulted.Simulation, Is.EqualTo("faulted"));
        Assert.That(faulted.IsLive || faulted.IsReady, Is.False);
        Assert.That(stalled.Simulation, Is.EqualTo("stalled"));
        Assert.That(stalled.IsLive || stalled.IsReady, Is.False);
    }

    [Test]
    public void Evaluate_BeforeTheFirstStatusOrWhileStopping_IsLiveButNotReady()
    {
        var starting = HealthReport.Evaluate(Snapshot(statusAgeSeconds: null), StallLimit);
        var stopping = HealthReport.Evaluate(Snapshot(isStopping: true, statusAgeSeconds: 30), StallLimit);

        Assert.That(starting.Simulation, Is.EqualTo("starting"));
        Assert.That(starting.IsLive && !starting.IsReady, Is.True);
        Assert.That(stopping.Simulation, Is.EqualTo("stopping"));
        Assert.That(stopping.IsLive && !stopping.IsReady, Is.True, "a server draining at shutdown is still live");
    }

    [Test]
    public void Evaluate_WithAdmissionClosedOrTheDatabaseAway_IsLiveButNotReady()
    {
        var closed = HealthReport.Evaluate(Snapshot(isAdmissionOpen: false), StallLimit);
        var away = HealthReport.Evaluate(Snapshot(database: DatabaseState.Unavailable), StallLimit);
        var migrating =
            HealthReport.Evaluate(Snapshot(database: DatabaseState.PendingMigrations), StallLimit);

        Assert.That(closed.Admission, Is.EqualTo("closed"));
        Assert.That(away.Database, Is.EqualTo("unavailable"));
        Assert.That(migrating.Database, Is.EqualTo("pending_migrations"));
        foreach (HealthReport report in new[] { closed, away, migrating })
        {
            Assert.That(report.IsLive && !report.IsReady, Is.True);
        }
    }

    [Test]
    public void Health_WhenNotEnabled_ServesNothing()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());
        using IHost host = TestHosts.CreateBuilder(new[] { "--Network:Port=0" }, root.Path).Build();

        host.Start();
        int port = host.Services.GetRequiredService<HealthEndpoint>().Port;
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(port, Is.Zero);
    }

    [Test]
    public void Live_AfterASimulationFault_Answers503()
    {
        using var root = new TemporaryDirectory();
        using var fail = new ManualResetEventSlim();
        using IHost host = StartHost(root, new InMemoryGameStore(), _ =>
        {
            if (fail.IsSet)
            {
                throw new InvalidOperationException("Simulated fault in a tick.");
            }
        });
        using HttpClient http = ClientFor(host);
        Assert.That(WaitFor(http, "/health/live", HttpStatusCode.OK, "ticking"), Is.True);

        fail.Set();
        bool isDown = WaitFor(http, "/health/live", HttpStatusCode.ServiceUnavailable, "\"simulation\":\"faulted\"");
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isDown, Is.True);
    }

    [Test]
    public void Ready_AfterAdmissionCloses_IsUnreadyWhileLive()
    {
        using var root = new TemporaryDirectory();
        using IHost host = StartHost(root, new InMemoryGameStore());
        using HttpClient http = ClientFor(host);
        Assert.That(WaitFor(http, "/health/ready", HttpStatusCode.OK, "ticking"), Is.True);

        host.Services.GetRequiredService<IServerTransport>().CloseAdmission();
        (HttpStatusCode ready, string body) = Get(http, "/health/ready");
        (HttpStatusCode live, string _) = Get(http, "/health/live");
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(ready, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(body, Does.Contain("\"admission\":\"closed\""));
        Assert.That(live, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void Ready_WhenTheDatabaseStopsAnswering_TurnsUnreadyWithoutAnyWork_AndBackWhenItReturns()
    {
        using var root = new TemporaryDirectory();
        var store = new InMemoryGameStore();
        using IHost host = StartHost(root, store);
        using HttpClient http = ClientFor(host);
        Assert.That(WaitFor(http, "/health/ready", HttpStatusCode.OK, "ticking"), Is.True);

        store.IsUnavailable = true;
        bool isUnready = WaitFor(http, "/health/ready", HttpStatusCode.ServiceUnavailable, "\"unavailable\"");
        (HttpStatusCode live, string _) = Get(http, "/health/live");
        store.IsUnavailable = false;
        bool isReadyAgain = WaitFor(http, "/health/ready", HttpStatusCode.OK, "\"available\"");
        host.StopAsync().GetAwaiter().GetResult();

        Assert.That(isUnready, Is.True, "the idle writer noticed the outage");
        Assert.That(live, Is.EqualTo(HttpStatusCode.OK), "the server still runs for the players in the world");
        Assert.That(isReadyAgain, Is.True);
    }

    [Test]
    public void Report_ToJson_NamesEveryCheckAndCarriesNoOtherText()
    {
        var report = HealthReport.Evaluate(Snapshot(isAdmissionOpen: false, pendingJobs: 250), StallLimit);

        string json = Encoding.UTF8.GetString(report.ToJson());

        Assert.That(
            json,
            Is.EqualTo(
                "{\"live\":true,\"ready\":false,\"checks\":{\"simulation\":\"ticking\",\"admission\":\"closed\","
                + "\"database\":\"available\",\"persistenceQueue\":\"degraded\"}}"));
    }
}
}
