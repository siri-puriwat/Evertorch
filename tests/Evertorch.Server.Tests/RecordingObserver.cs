using System;
using System.Collections.Generic;

namespace Evertorch.Server.Tests
{
internal sealed class RecordingObserver : ITickObserver
{
    public List<(uint Tick, TimeSpan Duration)> Completed { get; } = new List<(uint, TimeSpan)>();

    public List<(uint Tick, TimeSpan Duration, int SkippedSteps)> Overruns { get; } =
        new List<(uint, TimeSpan, int)>();

    public void OnTickCompleted(uint tick, TimeSpan duration)
    {
        Completed.Add((tick, duration));
    }

    public void OnOverrun(uint tick, TimeSpan duration, int skippedSteps)
    {
        Overruns.Add((tick, duration, skippedSteps));
    }
}
}
