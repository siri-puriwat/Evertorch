using System;
using System.Diagnostics;
using System.Threading;

namespace Evertorch.Server
{
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch m_stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => m_stopwatch.Elapsed;

    public void WaitUntil(TimeSpan elapsed, CancellationToken cancellation)
    {
        // The operating system may wake the thread late (about 15 ms on default Windows timer resolution). Callers
        // keep absolute deadlines, so a late wake-up delays one tick without slowing the long-run rate.
        while (!cancellation.IsCancellationRequested)
        {
            TimeSpan remaining = elapsed - m_stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            cancellation.WaitHandle.WaitOne(remaining);
        }
    }
}
}
