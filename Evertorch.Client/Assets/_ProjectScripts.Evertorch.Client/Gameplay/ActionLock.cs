using System;

namespace Evertorch.Client
{
/// <summary>
///     The local player's movement lock (Gameplay Systems §5.1): its own committed swing holds it, and from line 4b its
///     own cast. Each source sets its own span here, and <see cref="LocalPlayerDriver" /> applies the lock once a
///     tick, so one source ending never frees the other. It also counts down to the auto-attack's next swing, which a
///     skill request keeps clear of.
/// </summary>
public sealed class ActionLock
{
    /// <summary>
    ///     How many free ticks a request waits for once the hold here is over, so that the server's is over too (see
    ///     <see cref="HasBeenFreeFor" />).
    /// </summary>
    public const int ClearTicks = 2;

    private int m_swingTicks;
    private int m_castTicks;
    private int m_nextSwingTicks;
    private int m_freeTicks = int.MaxValue;
    private int m_castFreeTicks = int.MaxValue;
    private int m_castAwaitedTicks;

    /// <summary>
    ///     Whether the player's own swing still holds it; the auto-attack waits for this before it presses closer.
    /// </summary>
    public bool IsSwingLocked => m_swingTicks > 0;

    public bool IsCastLocked => m_castTicks > 0;

    /// <summary>
    ///     Whether a cast the player asked for may already be under way on the server, which the client has yet to
    ///     hear of: from the request until its <c>SkillCastStarted</c> or its refusal arrives, or its ticks pass.
    /// </summary>
    public bool IsCastAwaited => m_castAwaitedTicks > 0;

    /// <param name="ticks">How long the swing holds the player.</param>
    /// <param name="nextSwingTicks">When the auto-attack's next swing is expected; 0 for none.</param>
    public void LockForSwing(int ticks, int nextSwingTicks = 0)
    {
        m_swingTicks = Math.Max(0, ticks);
        m_nextSwingTicks = Math.Max(0, nextSwingTicks);
    }

    /// <summary>
    ///     Whether neither the swing nor the cast has held the player for the last <paramref name="ticks" /> ticks. The
    ///     hold here can end a tick before the server's, which ends a swing or a cast only after that tick's commands,
    ///     so a request sent the moment the hold ends could still find it under way.
    /// </summary>
    public bool HasBeenFreeFor(int ticks)
    {
        return !IsSwingLocked && !IsCastLocked && !IsCastAwaited && m_freeTicks >= ticks;
    }

    /// <summary>
    ///     Whether the player's own cast has not held it for the last <paramref name="ticks" /> ticks, whatever its
    ///     swing does.
    /// </summary>
    public bool HasBeenFreeOfCastFor(int ticks)
    {
        return !IsCastLocked && !IsCastAwaited && m_castFreeTicks >= ticks;
    }

    /// <summary>
    ///     Whether the auto-attack's next swing is expected within <paramref name="ticks" />: a request sent now could
    ///     reach the server after that swing began.
    /// </summary>
    public bool IsSwingDueWithin(int ticks)
    {
        return m_nextSwingTicks > 0 && m_nextSwingTicks <= ticks;
    }

    public void LockForCast(int ticks)
    {
        m_castTicks = Math.Max(0, ticks);
        m_castAwaitedTicks = 0;
    }

    /// <param name="ticks">How long to wait for word of the cast at most, should neither it nor a refusal come.</param>
    public void AwaitCast(int ticks)
    {
        m_castAwaitedTicks = Math.Max(0, ticks);
    }

    public void StopAwaitingCast()
    {
        m_castAwaitedTicks = 0;
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
        bool isCasting = m_castTicks > 0;
        bool isLocked = m_swingTicks > 0 || isCasting;
        if (m_swingTicks > 0)
        {
            m_swingTicks--;
        }

        if (m_castTicks > 0)
        {
            m_castTicks--;
        }

        if (m_nextSwingTicks > 0)
        {
            m_nextSwingTicks--;
        }

        if (m_castAwaitedTicks > 0)
        {
            m_castAwaitedTicks--;
        }

        if (isLocked)
        {
            m_freeTicks = 0;
        }
        else if (m_freeTicks < int.MaxValue)
        {
            m_freeTicks++;
        }

        if (isCasting)
        {
            m_castFreeTicks = 0;
        }
        else if (m_castFreeTicks < int.MaxValue)
        {
            m_castFreeTicks++;
        }

        return isLocked;
    }
}
}
