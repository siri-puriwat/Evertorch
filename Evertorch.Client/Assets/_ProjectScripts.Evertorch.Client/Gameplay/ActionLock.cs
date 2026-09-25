using System;

namespace Evertorch.Client
{
/// <summary>
///     The local player's movement lock (Gameplay Systems §5.1): its own committed swing holds it, and from line 4b its
///     own cast. Each source sets its own span here, and <see cref="LocalPlayerDriver" /> applies the lock once a
///     tick, so one source ending never frees the other.
/// </summary>
public sealed class ActionLock
{
    private int m_swingTicks;
    private int m_castTicks;

    /// <summary>
    ///     Whether the player's own swing still holds it; the auto-attack waits for this before it presses closer.
    /// </summary>
    public bool IsSwingLocked => m_swingTicks > 0;

    public bool IsCastLocked => m_castTicks > 0;

    public void LockForSwing(int ticks)
    {
        m_swingTicks = Math.Max(0, ticks);
    }

    public void LockForCast(int ticks)
    {
        m_castTicks = Math.Max(0, ticks);
    }

    /// <summary>
    ///     The cast ended before its time: the caster or its target died or left, or it was cancelled.
    /// </summary>
    public void EndCast()
    {
        m_castTicks = 0;
    }

    /// <summary>
    ///     Whether this tick is locked; counts every span down by one tick. Call once a tick.
    /// </summary>
    public bool Advance()
    {
        bool isLocked = m_swingTicks > 0 || m_castTicks > 0;
        if (m_swingTicks > 0)
        {
            m_swingTicks--;
        }

        if (m_castTicks > 0)
        {
            m_castTicks--;
        }

        return isLocked;
    }
}
}
