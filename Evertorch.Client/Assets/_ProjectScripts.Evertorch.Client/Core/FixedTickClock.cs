using System;

namespace Evertorch.Client
{
/// <summary>
///     Turns variable frame time into whole simulation ticks at the server's rate, plus how far the current frame sits
///     between two ticks.
/// </summary>
public sealed class FixedTickClock
{
    // After a long stall the backlog is dropped rather than simulated, which would only send a burst of input the
    // server discards anyway.
    public const int MaxTicksPerAdvance = 5;

    private const float MaxFrameSeconds = 10f;

    private readonly float m_tickSeconds;
    private float m_accumulated;

    public FixedTickClock(float tickSeconds)
    {
        if (!(tickSeconds > 0f) || float.IsInfinity(tickSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(tickSeconds));
        }

        m_tickSeconds = tickSeconds;
    }

    public uint Tick { get; private set; }

    public float Alpha => m_accumulated / m_tickSeconds;

    public int SkippedTicks { get; private set; }

    /// <summary>
    ///     Adds frame time and returns how many ticks are due. Call <see cref="NextTick" /> once for each.
    /// </summary>
    public int Advance(float deltaSeconds)
    {
        if (!(deltaSeconds > 0f) || float.IsInfinity(deltaSeconds))
        {
            return 0;
        }

        m_accumulated += Math.Min(deltaSeconds, MaxFrameSeconds);
        int due = (int)(m_accumulated / m_tickSeconds);
        m_accumulated -= due * m_tickSeconds;
        if (due > MaxTicksPerAdvance)
        {
            SkippedTicks += due - MaxTicksPerAdvance;
            due = MaxTicksPerAdvance;
        }

        return due;
    }

    public uint NextTick()
    {
        Tick = unchecked(Tick + 1);
        return Tick;
    }
}
}
