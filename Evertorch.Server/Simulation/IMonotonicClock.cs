using System;
using System.Threading;

namespace Evertorch.Server
{
/// <summary>
///     Time that only moves forward and is unrelated to the wall clock. The simulation reads no other time source.
/// </summary>
public interface IMonotonicClock
{
    TimeSpan Elapsed { get; }

    /// <summary>
    ///     Blocks until <see cref="Elapsed" /> reaches <paramref name="elapsed" /> or cancellation is requested. It returns
    ///     instead of throwing when cancelled.
    /// </summary>
    void WaitUntil(TimeSpan elapsed, CancellationToken cancellation);
}
}
