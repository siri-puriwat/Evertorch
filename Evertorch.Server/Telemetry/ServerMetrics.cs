using System;

namespace Evertorch.Server
{
/// <summary>
/// Tick timing as seen by the loop. Written by the tick thread only; other threads see it through the status the
/// tick thread publishes, never by reading these fields.
/// </summary>
public sealed class ServerMetrics : ITickObserver
{
    private readonly TickLogObserver m_log;

    public ServerMetrics(TickLogObserver log)
    {
        m_log = log;
    }

    public uint LastTick { get; private set; }

    public TimeSpan LastTickDuration { get; private set; }

    public TimeSpan MaxTickDuration { get; private set; }

    public long Overruns { get; private set; }

    public long SkippedSteps { get; private set; }

    public void OnTickCompleted(uint tick, TimeSpan duration)
    {
        LastTick = tick;
        LastTickDuration = duration;
        if (duration > MaxTickDuration)
        {
            MaxTickDuration = duration;
        }

        m_log.OnTickCompleted(tick, duration);
    }

    public void OnOverrun(uint tick, TimeSpan duration, int skippedSteps)
    {
        Overruns++;
        SkippedSteps += skippedSteps;
        m_log.OnOverrun(tick, duration, skippedSteps);
    }
}
}
