using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One entity's combat state: whether it keeps attacking its target, the swing in progress, the schedule that
///     paces its swings (Gameplay Systems §7), and its cast, after-cast delay, and skill cooldowns (§9). Only the tick
///     thread reads or writes it.
/// </summary>
public sealed class CombatState
{
    private readonly Dictionary<SkillDefinitionId, long> m_cooldownEnds = new();

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

    /// <summary>
    ///     From the tick a cast begins until it resolves or is interrupted; the caster holds still meanwhile.
    /// </summary>
    public bool IsCasting { get; private set; }

    public SkillDefinitionId CastSkill { get; private set; }

    /// <summary>
    ///     The entity the cast resolves on: the caster itself for a skill on itself.
    /// </summary>
    public EntityId CastTarget { get; private set; }

    public long CastResolveMs { get; private set; }

    /// <summary>
    ///     Whether the cast already paid its SP when it began.
    /// </summary>
    public bool IsCastPaid { get; private set; }

    /// <summary>
    ///     The end of the after-cast delay: no swing or cast begins before it.
    /// </summary>
    public long DelayEndsMs { get; private set; } = long.MinValue;

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

    public void BeginCast(SkillDefinitionId skill, EntityId target, long resolveMs, bool isPaid)
    {
        IsCasting = true;
        CastSkill = skill;
        CastTarget = target;
        CastResolveMs = resolveMs;
        IsCastPaid = isPaid;
    }

    /// <summary>
    ///     Ends the cast without effect: no after-cast delay and no cooldown follow.
    /// </summary>
    public void EndCast()
    {
        IsCasting = false;
        CastSkill = default;
        CastTarget = default;
        IsCastPaid = false;
    }

    /// <summary>
    ///     The cast resolved at <paramref name="nowMs" />: its after-cast delay and its skill's cooldown start now.
    /// </summary>
    public void CompleteCast(long nowMs, int afterCastDelayMs, int cooldownMs)
    {
        m_cooldownEnds[CastSkill] = nowMs + cooldownMs;
        DelayEndsMs = nowMs + afterCastDelayMs;
        EndCast();
    }

    /// <summary>
    ///     When <paramref name="skill" /> may be cast again; <see cref="long.MinValue" /> when it never was.
    /// </summary>
    public long CooldownEndMs(SkillDefinitionId skill)
    {
        return m_cooldownEnds.TryGetValue(skill, out long end) ? end : long.MinValue;
    }
}
}
