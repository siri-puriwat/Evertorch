using System;

namespace Evertorch.Server
{
/// <summary>
///     A token bucket counted in ticks rather than in time (Network Protocol §11), so it behaves the same at any tick
///     rate and in tests that step ticks by hand. Tick thread only.
/// </summary>
public sealed class TickBucket
{
    private readonly double m_perTick;
    private readonly double m_burst;
    private double m_tokens;
    private uint m_tick;

    public TickBucket(double perSecond, int burst, int tickRate, uint tick)
    {
        m_perTick = perSecond / tickRate;
        m_burst = burst;
        m_tokens = burst;
        m_tick = tick;
    }

    public bool TryTake(uint tick)
    {
        uint elapsed = unchecked(tick - m_tick);
        if (elapsed > 0)
        {
            m_tokens = Math.Min(m_burst, m_tokens + elapsed * m_perTick);
            m_tick = tick;
        }

        if (m_tokens < 1d)
        {
            return false;
        }

        m_tokens -= 1d;
        return true;
    }
}
}
