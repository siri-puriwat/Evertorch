using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One entity's basic-attack state: whether it keeps attacking its target, the swing in progress, and the
///     schedule that paces its swings (Gameplay Systems §7). Only the tick thread reads or writes it.
/// </summary>
public sealed class CombatState
{
    /// <summary>
    ///     Whether the entity keeps swinging at its target. Cancelling, clearing, or changing the target ends it; a
    ///     swing already begun still resolves.
    /// </summary>
    public bool IsAutoAttacking { get; set; }

    /// <summary>
    ///     From the tick a swing begins until its impact resolves, which is also the movement lock.
    /// </summary>
    public bool IsSwinging { get; private set; }

    public EntityId SwingTarget { get; private set; }

    public uint SwingStartTick { get; private set; }

    public long ImpactMs { get; private set; }

    /// <summary>
    ///     The nominal start of the next swing: the previous nominal start plus the interval. Nothing but a swing
    ///     start moves it, so no cancel, clear, or retarget lets a swing come early.
    /// </summary>
    public long NextNominalMs { get; private set; } = long.MinValue;

    /// <summary>
    ///     Whether the movement phase moved the entity this tick; a swing only begins on a tick it did not.
    /// </summary>
    public bool HasMovedThisTick { get; set; }

    public void BeginSwing(EntityId target, uint tick, long nominalStartMs, AttackTiming timing)
    {
        IsSwinging = true;
        SwingTarget = target;
        SwingStartTick = tick;
        ImpactMs = nominalStartMs + (long)timing.Impact.TotalMilliseconds;
        NextNominalMs = nominalStartMs + (long)timing.Interval.TotalMilliseconds;
    }

    public void EndSwing()
    {
        IsSwinging = false;
        SwingTarget = default;
    }
}
}
