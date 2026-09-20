using System;

namespace Evertorch.Server
{
public interface ITickObserver
{
    void OnTickCompleted(uint tick, TimeSpan duration);

    /// <summary>
    /// Called when a tick ran longer than one step, or when the loop fell so far behind that it gave up
    /// <paramref name="skippedSteps"/> steps of real time instead of simulating them back to back.
    /// </summary>
    void OnOverrun(uint tick, TimeSpan duration, int skippedSteps);
}
}
