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
    private readonly RateLimitedLog m_overruns;

    public TickLogObserver(ILogger<TickLogObserver> logger, IMonotonicClock clock)
    {
        m_logger = logger;
        m_overruns = new RateLimitedLog(clock, MinimumLogInterval);
    }

    public void OnTickCompleted(uint tick, TimeSpan duration)
    {
    }

    public void OnOverrun(uint tick, TimeSpan duration, int skippedSteps)
    {
        // A stalled server overruns every tick; logging each one would add load exactly when there is none to spare.
        if (m_overruns.TryEnter(out int suppressed))
        {
            LogOverrun(m_logger, tick, duration.TotalMilliseconds, skippedSteps, suppressed, null);
        }
    }
}
}
