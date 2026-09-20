using System;
using System.Threading;

namespace Evertorch.Server.Tests
{
internal sealed class FakeClock : IMonotonicClock
{
    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan duration)
    {
        Elapsed += duration;
    }

    public void WaitUntil(TimeSpan elapsed, CancellationToken cancellation)
    {
        if (!cancellation.IsCancellationRequested && elapsed > Elapsed)
        {
            Elapsed = elapsed;
        }
    }
}
}
