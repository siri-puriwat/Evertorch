using System;

namespace Evertorch.Client
{
/// <summary>
///     A steady guess at the server time of the newest state the client holds. It runs on the local clock and leans
///     gently toward what snapshots report, so jitter in their arrival does not make remote entities stutter.
/// </summary>
public sealed class ServerTimeEstimator
{
    public const double ResetThresholdSeconds = 0.5;

    private const double Gain = 0.1;

    private bool m_hasObservation;

    public double Now { get; private set; }

    public void Observe(double serverTimeSeconds)
    {
        double error = serverTimeSeconds - Now;
        if (!m_hasObservation || Math.Abs(error) > ResetThresholdSeconds)
        {
            Now = serverTimeSeconds;
            m_hasObservation = true;
            return;
        }

        Now += error * Gain;
    }

    public void Advance(double deltaSeconds)
    {
        if (m_hasObservation && deltaSeconds > 0.0)
        {
            Now += deltaSeconds;
        }
    }
}
}
