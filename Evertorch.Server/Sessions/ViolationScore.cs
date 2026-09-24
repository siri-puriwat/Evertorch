using System;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     One connection's violation score (Network Protocol §11). Each violation adds <see cref="Points" />, and the score
///     decays by <c>Abuse:ViolationDecayPerSecond</c> spread over the ticks, settled whenever it changes. Counted in
///     ticks like the command buckets. Tick thread only.
/// </summary>
public sealed class ViolationScore
{
    /// <summary>
    ///     Ten violations at once reach the default threshold, and the default decay forgives one every two seconds.
    /// </summary>
    public const int Points = 10;

    private readonly double m_threshold;
    private readonly double m_decayPerTick;
    private uint m_tick;

    public ViolationScore(AbuseOptions options, int tickRate, uint tick)
    {
        m_threshold = options.ViolationThreshold;
        m_decayPerTick = (double)options.ViolationDecayPerSecond / tickRate;
        m_tick = tick;
    }

    /// <summary>
    ///     The score as of the last violation.
    /// </summary>
    public double Value { get; private set; }

    /// <summary>
    ///     <c>None</c> until the score reaches the threshold; then <c>RateLimited</c> if rate excess took it there, and
    ///     <c>Kicked</c> otherwise.
    /// </summary>
    public DisconnectReason Verdict { get; private set; }

    public void Add(Violation violation, int count, uint tick)
    {
        uint elapsed = unchecked(tick - m_tick);
        if (elapsed > 0)
        {
            Value = Math.Max(0d, Value - elapsed * m_decayPerTick);
            m_tick = tick;
        }

        Value += (double)count * Points;
        if (Verdict == DisconnectReason.None && Value >= m_threshold)
        {
            Verdict = violation == Violation.InputRate || violation == Violation.CommandRate
                ? DisconnectReason.RateLimited
                : DisconnectReason.Kicked;
        }
    }
}
}
