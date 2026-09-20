using System;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
public sealed class TickLogObserver : ITickObserver
{
    private static readonly TimeSpan MinimumLogInterval = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, uint, double, int, int, Exception?> LogOverrun =
        LoggerMessage.Define<uint, double, int, int>(
            LogLevel.Warning,
            new EventId(1001, "TickOverrun"),
            "Tick {Tick} took {DurationMs} ms and skipped {SkippedSteps} steps; {Suppressed} earlier overruns unlogged.");

    private readonly ILogger<TickLogObserver> m_logger;
    private readonly IMonotonicClock m_clock;
    private TimeSpan? m_lastLogged;
    private int m_suppressed;

    public TickLogObserver(ILogger<TickLogObserver> logger, IMonotonicClock clock)
    {
        m_logger = logger;
        m_clock = clock;
    }

    public void OnTickCompleted(uint tick, TimeSpan duration)
    {
    }

    public void OnOverrun(uint tick, TimeSpan duration, int skippedSteps)
    {
        // A stalled server overruns every tick; logging each one would add load exactly when there is none to spare.
        TimeSpan now = m_clock.Elapsed;
        if (m_lastLogged.HasValue && now - m_lastLogged.Value < MinimumLogInterval)
        {
            m_suppressed++;
            return;
        }

        LogOverrun(m_logger, tick, duration.TotalMilliseconds, skippedSteps, m_suppressed, null);
        m_lastLogged = now;
        m_suppressed = 0;
    }
}
}
