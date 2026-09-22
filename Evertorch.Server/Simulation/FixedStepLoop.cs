using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class FixedStepLoop
{
    private readonly IMonotonicClock m_clock;
    private readonly TickPipeline m_pipeline;
    private readonly ITickObserver m_observer;
    private readonly TimeSpan m_step;
    private readonly float m_deltaSeconds;
    private readonly int m_maxCatchUpTicks;

    public FixedStepLoop(
        IMonotonicClock clock,
        TickPipeline pipeline,
        IOptions<SimulationOptions> options,
        ITickObserver observer)
    {
        SimulationOptions simulation = options.Value;
        if (simulation.TickRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The tick rate must be positive.");
        }

        m_clock = clock;
        m_pipeline = pipeline;
        m_observer = observer;
        m_step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / simulation.TickRate);
        m_deltaSeconds = 1f / simulation.TickRate;
        m_maxCatchUpTicks = simulation.MaxCatchUpTicks;
    }

    /// <summary>
    ///     Runs ticks until <paramref name="stop" /> is signalled. A tick that has started always finishes.
    /// </summary>
    public void Run(CancellationToken stop)
    {
        // Deadlines are absolute so waiting error never accumulates into rate drift.
        TimeSpan deadline = m_clock.Elapsed;
        uint tick = 0;

        while (!stop.IsCancellationRequested)
        {
            m_clock.WaitUntil(deadline, stop);
            if (stop.IsCancellationRequested)
            {
                return;
            }

            tick++;
            TimeSpan started = m_clock.Elapsed;
            m_pipeline.Execute(new TickContext(tick, m_deltaSeconds));
            TimeSpan duration = m_clock.Elapsed - started;
            m_observer.OnTickCompleted(tick, duration);

            deadline += m_step;
            int skippedSteps = SkipBacklogBeyondCatchUpLimit(ref deadline);
            if (duration > m_step || skippedSteps > 0)
            {
                m_observer.OnOverrun(tick, duration, skippedSteps);
            }
        }
    }

    // A short backlog is simulated back to back. A long one is abandoned, because replaying it would keep the
    // server behind for longer than the stall itself; the observer hears how much real time was given up.
    private int SkipBacklogBeyondCatchUpLimit(ref TimeSpan deadline)
    {
        TimeSpan behind = m_clock.Elapsed - deadline;
        if (behind.Ticks <= m_step.Ticks * m_maxCatchUpTicks)
        {
            return 0;
        }

        long skippedSteps = behind.Ticks / m_step.Ticks;
        deadline += TimeSpan.FromTicks(m_step.Ticks * skippedSteps);
        return (int)Math.Min(skippedSteps, int.MaxValue);
    }
}
}
