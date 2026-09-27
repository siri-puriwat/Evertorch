using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Whether the server is live and ready (System Architecture §10), for the health endpoints and the gateway's sign-in,
///     which is refused by the same test. Everything it reads is safe from any thread: the published status, flags, and
///     the writer's counters.
/// </summary>
public sealed class HealthProbe
{
    private static readonly Action<ILogger, int, int, int, Exception?> LogQueueDegraded =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Warning,
            new EventId(6004, "PersistenceQueueDegraded"),
            "{Pending} of {Capacity} persistence jobs are waiting; the database is not keeping up ({Suppressed} held "
            + "back).");

    private readonly int m_queueCapacity;
    private readonly TimeSpan m_stallLimit;
    private readonly ServerLifetimeService m_server;
    private readonly StatusPublisher m_status;
    private readonly IMonotonicClock m_clock;
    private readonly IServerTransport m_transport;
    private readonly PersistenceWorker m_writer;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly RateLimitedLog m_degradedLog;
    private readonly ILogger<HealthProbe> m_logger;

    public HealthProbe(
        IOptions<HealthOptions> options,
        IOptions<PersistenceOptions> persistence,
        ServerLifetimeService server,
        StatusPublisher status,
        IMonotonicClock clock,
        IServerTransport transport,
        PersistenceWorker writer,
        IHostApplicationLifetime lifetime,
        ILogger<HealthProbe> logger)
    {
        m_queueCapacity = persistence.Value.QueueCapacity;
        m_stallLimit = TimeSpan.FromMilliseconds(options.Value.LivenessStallMs);
        m_server = server;
        m_status = status;
        m_clock = clock;
        m_transport = transport;
        m_writer = writer;
        m_lifetime = lifetime;
        m_degradedLog = new RateLimitedLog(clock, TimeSpan.FromSeconds(30));
        m_logger = logger;
    }

    public HealthReport Evaluate()
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
}
}
