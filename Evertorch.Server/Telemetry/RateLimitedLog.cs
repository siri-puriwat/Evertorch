using System;

namespace Evertorch.Server
{
/// <summary>
///     Lets at most a given number of events through per interval and counts the ones it holds back, so a flood of the
///     same event costs a few lines per interval, with the number held back. Safe to share between threads.
/// </summary>
public sealed class RateLimitedLog
{
    private readonly IMonotonicClock m_clock;
    private readonly TimeSpan m_interval;
    private readonly int m_eventsPerInterval;
    private readonly object m_gate = new();
    private TimeSpan? m_intervalStart;
    private int m_entered;
    private int m_suppressed;

    public RateLimitedLog(IMonotonicClock clock, TimeSpan interval, int eventsPerInterval = 1)
    {
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
        m_interval = interval;
        m_eventsPerInterval = eventsPerInterval;
    }

    /// <summary>
    ///     True when the caller should log now; <paramref name="suppressed" /> is then the number held back since the
    ///     last one that went through.
    /// </summary>
    public bool TryEnter(out int suppressed)
    {
        TimeSpan now = m_clock.Elapsed;
        lock (m_gate)
        {
            if (!m_intervalStart.HasValue || now - m_intervalStart.Value >= m_interval)
            {
                m_intervalStart = now;
                m_entered = 0;
            }

            if (m_entered >= m_eventsPerInterval)
            {
                m_suppressed++;
                suppressed = 0;
                return false;
            }

            m_entered++;
            suppressed = m_suppressed;
            m_suppressed = 0;
            return true;
        }
    }
}
}
