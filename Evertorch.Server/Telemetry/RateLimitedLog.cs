using System;

namespace Evertorch.Server
{
/// <summary>
///     Lets one event through at most once per interval and counts the ones it holds back, so a flood of the same event
///     costs one line per interval, with the number it stood for. Safe to share between threads.
/// </summary>
public sealed class RateLimitedLog
{
    private readonly IMonotonicClock m_clock;
    private readonly TimeSpan m_interval;
    private readonly object m_gate = new();
    private TimeSpan? m_lastLogged;
    private int m_suppressed;

    public RateLimitedLog(IMonotonicClock clock, TimeSpan interval)
    {
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
        m_interval = interval;
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
            if (m_lastLogged.HasValue && now - m_lastLogged.Value < m_interval)
            {
                m_suppressed++;
                suppressed = 0;
                return false;
            }

            m_lastLogged = now;
            suppressed = m_suppressed;
            m_suppressed = 0;
            return true;
        }
    }
}
}
