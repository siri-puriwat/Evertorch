using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Serves <c>/health/live</c> and <c>/health/ready</c> over plain HTTP (System Architecture §10), with Kestrel in a
///     host of its own. The only place the server touches ASP.NET Core.
/// </summary>
/// <remarks>
///     It is registered before <see cref="ServerLifetimeService" />, so it starts before the transport and stops after
///     everything else. Its host has no lifetime, so Ctrl+C reaches only the server's, and it logs through the server's
///     logger factory.
/// </remarks>
public sealed class HealthEndpoint : IHostedService, IDisposable
{
    private const string LivePath = "/health/live";
    private const string ReadyPath = "/health/ready";

    private static readonly Action<ILogger, string, int, Exception?> LogListening =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(6003, "HealthListening"),
            "Health endpoints listening on http://{Address}:{Port}.");

    private static readonly Action<ILogger, int, int, int, Exception?> LogQueueDegraded =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Warning,
            new EventId(6004, "PersistenceQueueDegraded"),
            "{Pending} of {Capacity} persistence jobs are waiting; the database is not keeping up ({Suppressed} held "
            + "back).");

    private readonly HealthOptions m_options;
    private readonly int m_queueCapacity;
    private readonly TimeSpan m_stallLimit;
    private readonly ILoggerFactory m_loggers;
    private readonly ServerLifetimeService m_server;
    private readonly StatusPublisher m_status;
    private readonly IMonotonicClock m_clock;
    private readonly IServerTransport m_transport;
    private readonly PersistenceWorker m_writer;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly RateLimitedLog m_degradedLog;
    private readonly ILogger<HealthEndpoint> m_logger;
    private WebApplication? m_app;

    public HealthEndpoint(
        IOptions<HealthOptions> options,
        IOptions<PersistenceOptions> persistence,
        ILoggerFactory loggers,
        ServerLifetimeService server,
        StatusPublisher status,
        IMonotonicClock clock,
        IServerTransport transport,
        PersistenceWorker writer,
        IHostApplicationLifetime lifetime,
        ILogger<HealthEndpoint> logger)
    {
        m_options = options.Value;
        m_queueCapacity = persistence.Value.QueueCapacity;
        m_stallLimit = TimeSpan.FromMilliseconds(m_options.LivenessStallMs);
        m_loggers = loggers;
        m_server = server;
        m_status = status;
        m_clock = clock;
        m_transport = transport;
        m_writer = writer;
        m_lifetime = lifetime;
        m_degradedLog = new RateLimitedLog(clock, TimeSpan.FromSeconds(30));
        m_logger = logger;
    }

    /// <summary>
    ///     The port actually bound, which differs from the configured one when that is 0; 0 while nothing listens.
    /// </summary>
    public int Port { get; private set; }

    public void Dispose()
    {
        m_app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!m_options.Enabled)
        {
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Parse(m_options.BindAddress), m_options.Port));
        builder.Services.AddSingleton<IHostLifetime, NoLifetime>();
        builder.Services.AddSingleton(m_loggers);
        WebApplication app = builder.Build();
        app.Run(AnswerAsync);
        m_app = app;

        // A port that cannot be bound fails the server's start, like the game port (System Architecture §13).
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Port = new Uri(app.Urls.First()).Port;
        LogListening(m_logger, m_options.BindAddress, Port, null);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (m_app != null)
        {
            await m_app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AnswerAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        bool isLivePath = string.Equals(path, LivePath, StringComparison.Ordinal);
        if (!isLivePath && !string.Equals(path, ReadyPath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        HealthReport report = Evaluate();
        bool isHealthy = isLivePath ? report.IsLive : report.IsReady;
        context.Response.StatusCode = isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.Body.WriteAsync(report.ToJson(), context.RequestAborted).ConfigureAwait(false);
    }

    // Everything read here is safe from any thread: the published status, flags, and the writer's counters.
    private HealthReport Evaluate()
    {
        ServerStatus status = m_status.Current;
        var snapshot = new HealthSnapshot
        {
            HasFaulted = m_server.HasFaulted,
            IsStopping = m_lifetime.ApplicationStopping.IsCancellationRequested,
            StatusAge = status.Tick == 0 ? null : m_clock.Elapsed - status.PublishedAt,
            IsAdmissionOpen = m_transport.IsAdmissionOpen,
            Database = m_writer.State,
            IsDatabaseAvailable = m_writer.IsAvailable,
            PendingJobs = m_writer.PendingJobs,
            QueueCapacity = m_queueCapacity
        };
        var report = HealthReport.Evaluate(snapshot, m_stallLimit);
        if (report.IsQueueDegraded && m_degradedLog.TryEnter(out int suppressed))
        {
            LogQueueDegraded(m_logger, snapshot.PendingJobs, m_queueCapacity, suppressed, null);
        }

        return report;
    }

    // The server's own lifetime answers Ctrl+C; this host only starts and stops when the server says so.
    private sealed class NoLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
}
