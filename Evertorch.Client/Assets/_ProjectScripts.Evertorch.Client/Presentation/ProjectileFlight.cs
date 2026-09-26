using System;

namespace Evertorch.Client
{
/// <summary>
///     When a projectile flies (Gameplay Systems §8): it arrives at the server's impact or resolution, and it is in the
///     air for the last <see cref="MaxSeconds" /> before, or from the start when the attack or cast is shorter.
/// </summary>
public readonly struct ProjectileFlight
{
    public const double MaxSeconds = 0.4;

    public ProjectileFlight(double startSeconds, double impactSeconds)
    {
        ImpactSeconds = impactSeconds;
        LaunchSeconds = impactSeconds - Math.Max(0d, Math.Min(MaxSeconds, impactSeconds - startSeconds));
    }

    public double LaunchSeconds { get; }

    public double ImpactSeconds { get; }

    /// <summary>
    ///     How far the projectile has flown at <paramref name="now" />, 0 at the launch; false before the launch and
    ///     from the impact on.
    /// </summary>
    public bool TryGetProgress(double now, out float progress)
    {
        progress = 0f;
        if (now < LaunchSeconds || now >= ImpactSeconds)
        {
            return false;
        }

        progress = (float)((now - LaunchSeconds) / (ImpactSeconds - LaunchSeconds));
        return true;
    }
}
}
