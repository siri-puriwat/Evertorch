using System;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     The procedural attack curves (Gameplay Systems §8). They only shape what is drawn, from the timing the server
///     sent; nothing reads them back.
/// </summary>
public static class CombatAnimation
{
    /// <summary>
    ///     The shortest return from the lunge, so a swing with no recovery still eases back.
    /// </summary>
    public const double MinimumReturnSeconds = 0.1;

    public const double SquashSeconds = 0.25;

    public const double NumberSeconds = 1.0;

    /// <summary>
    ///     How far into the lunge the attacker is: 0 at the swing's start, 1 exactly at impact, back to 0 by the end
    ///     of the recovery, and never later than the interval.
    /// </summary>
    public static float LungeWeight(double elapsedSeconds, AttackTiming timing)
    {
        double impact = timing.Impact.TotalSeconds;
        double interval = timing.Interval.TotalSeconds;
        double end = Math.Min(interval, impact + Math.Max(timing.Recovery.TotalSeconds, MinimumReturnSeconds));
        if (elapsedSeconds < 0.0)
        {
            return 0f;
        }

        if (elapsedSeconds <= impact)
        {
            return impact <= 0.0 ? 1f : SmoothStep(elapsedSeconds / impact);
        }

        return elapsedSeconds >= end ? 0f : SmoothStep((end - elapsedSeconds) / (end - impact));
    }

    /// <summary>
    ///     How squashed a target is after a hit lands: 1 at the impact, gone <see cref="SquashSeconds" /> later.
    /// </summary>
    public static float SquashWeight(double sinceImpactSeconds)
    {
        if (sinceImpactSeconds < 0.0 || sinceImpactSeconds >= SquashSeconds)
        {
            return 0f;
        }

        return (float)(1.0 - sinceImpactSeconds / SquashSeconds);
    }

    private static float SmoothStep(double t)
    {
        double clamped = Math.Max(0.0, Math.Min(1.0, t));
        return (float)(clamped * clamped * (3.0 - 2.0 * clamped));
    }
}
}
