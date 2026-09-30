using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     When each combat event is shown. Remote entities are drawn in the past, so their events are placed at the
///     server tick on the interpolated timeline; the local player's are shown as they arrive, on the same clock its
///     lock counts from. It decides nothing about the fight itself.
/// </summary>
public sealed class CombatTimeline
{
    private readonly Dictionary<EntityId, Swing> m_swings = new();
    private readonly Dictionary<EntityId, Moment> m_impacts = new();
    private readonly Dictionary<EntityId, Moment> m_deaths = new();
    private readonly Dictionary<EntityId, Cast> m_casts = new();
    private readonly Dictionary<EntityId, SkillUse> m_skills = new();
    private readonly List<HitMark> m_pending = new();

    public int PendingHits => m_pending.Count;

    public void BeginSwing(EntityId attacker, double startSeconds, AttackTiming timing, bool isOnLocalClock)
    {
        m_swings[attacker] = new Swing(new Moment(startSeconds, isOnLocalClock), timing);
    }

    /// <summary>
    ///     A cast of <paramref name="castSeconds" /> began; one of no length shows no bar, though its caster's body still
    ///     plays the skill (Gameplay Systems §8).
    /// </summary>
    public void BeginCast(
        EntityId caster,
        EntityId target,
        double startSeconds,
        double castSeconds,
        bool isOnLocalClock,
        SkillDefinitionId skill = default)
    {
        m_skills[caster] = new SkillUse(new Moment(startSeconds, isOnLocalClock), Math.Max(castSeconds, 0.0), skill);
        if (castSeconds > 0.0)
        {
            m_casts[caster] = new Cast(new Moment(startSeconds, isOnLocalClock), castSeconds, target);
        }
        else
        {
            m_casts.Remove(caster);
        }
    }

    public void EndCast(EntityId caster)
    {
        m_casts.Remove(caster);
        m_skills.Remove(caster);
    }

    /// <summary>
    ///     Ends every cast <paramref name="entity" /> was making or was the target of: it died or left.
    /// </summary>
    public void EndCastsOf(EntityId entity)
    {
        m_casts.Remove(entity);
        var casters = m_casts.Where(pair => pair.Value.Target == entity).Select(pair => pair.Key).ToList();
        foreach (EntityId caster in casters)
        {
            m_casts.Remove(caster);
            if (m_skills.TryGetValue(caster, out SkillUse use) && use.CastSeconds > 0.0)
            {
                m_skills.Remove(caster);
            }
        }

        m_skills.Remove(entity);
    }

    /// <summary>
    ///     The entity's latest swing: how long ago it began, which is negative before its moment, and its timing.
    /// </summary>
    public bool TryGetSwing(EntityId entity, double localNow, double remoteNow, out double since,
        out AttackTiming timing)
    {
        if (m_swings.TryGetValue(entity, out Swing swing))
        {
            since = swing.Start.Since(localNow, remoteNow);
            timing = swing.Timing;
            return true;
        }

        since = 0.0;
        timing = default;
        return false;
    }

    /// <summary>
    ///     The entity's latest skill, cast or instant: how long ago it began, its cast time, and the skill.
    /// </summary>
    public bool TryGetSkill(
        EntityId entity,
        double localNow,
        double remoteNow,
        out double since,
        out double castSeconds,
        out SkillDefinitionId skill)
    {
        if (m_skills.TryGetValue(entity, out SkillUse use))
        {
            since = use.Start.Since(localNow, remoteNow);
            castSeconds = use.CastSeconds;
            skill = use.Skill;
            return true;
        }

        since = 0.0;
        castSeconds = 0.0;
        skill = default;
        return false;
    }

    /// <summary>
    ///     How long ago the entity was last hit, a miss or a heal not counting.
    /// </summary>
    public bool TryGetLastHit(EntityId entity, double localNow, double remoteNow, out double since)
    {
        since = m_impacts.TryGetValue(entity, out Moment impact) ? impact.Since(localNow, remoteNow) : 0.0;
        return m_impacts.ContainsKey(entity);
    }

    /// <summary>
    ///     How long the entity has been shown dead: infinite when the client never saw it die.
    /// </summary>
    public double DeadSince(EntityId entity, double localNow, double remoteNow)
    {
        return m_deaths.TryGetValue(entity, out Moment death)
            ? death.Since(localNow, remoteNow)
            : double.PositiveInfinity;
    }

    /// <summary>
    ///     How far along the caster's cast is drawn, from 0 to 1; false while it shows none.
    /// </summary>
    public bool TryGetCastProgress(EntityId caster, double localNow, double remoteNow, out float progress)
    {
        progress = 0f;
        if (!m_casts.TryGetValue(caster, out Cast cast))
        {
            return false;
        }

        double since = cast.Start.Since(localNow, remoteNow);
        if (since < 0.0 || since >= cast.Seconds)
        {
            return false;
        }

        progress = (float)(since / cast.Seconds);
        return true;
    }

    public void AddHit(HitMark hit)
    {
        m_pending.Add(hit);
        if (hit.Result != CombatResult.Miss && !hit.IsHeal)
        {
            m_impacts[hit.Target] = new Moment(hit.Seconds, hit.IsOnLocalClock);
        }
    }

    public void MarkDeath(EntityId entity, double atSeconds, bool isOnLocalClock)
    {
        m_deaths[entity] = new Moment(atSeconds, isOnLocalClock);
    }

    /// <summary>
    ///     A body that died no longer swings: its clip turns to its death (Gameplay Systems §8).
    /// </summary>
    public void EndSwing(EntityId entity)
    {
        m_swings.Remove(entity);
    }

    public void ClearDeath(EntityId entity)
    {
        m_deaths.Remove(entity);
    }

    public float Lunge(EntityId entity, double localNow, double remoteNow)
    {
        return m_swings.TryGetValue(entity, out Swing swing)
            ? CombatAnimation.LungeWeight(swing.Start.Since(localNow, remoteNow), swing.Timing)
            : 0f;
    }

    public float Squash(EntityId entity, double localNow, double remoteNow)
    {
        return m_impacts.TryGetValue(entity, out Moment impact)
            ? CombatAnimation.SquashWeight(impact.Since(localNow, remoteNow))
            : 0f;
    }

    /// <summary>
    ///     Whether a dead entity is drawn dead yet: from its death on its own timeline, or at once when the client
    ///     never saw it die.
    /// </summary>
    public bool IsShownDead(EntityId entity, bool isDead, double localNow, double remoteNow)
    {
        return isDead && (!m_deaths.TryGetValue(entity, out Moment death) || death.Since(localNow, remoteNow) >= 0.0);
    }

    /// <summary>
    ///     Moves the hits whose moment has come into <paramref name="due" />, in the order they arrived.
    /// </summary>
    public void CollectDueHits(double localNow, double remoteNow, List<HitMark> due)
    {
        int kept = 0;
        for (int index = 0; index < m_pending.Count; index++)
        {
            HitMark hit = m_pending[index];
            double now = hit.IsOnLocalClock ? localNow : remoteNow;
            if (now >= hit.Seconds)
            {
                due.Add(hit);
            }
            else
            {
                m_pending[kept++] = hit;
            }
        }

        m_pending.RemoveRange(kept, m_pending.Count - kept);
    }

    public void Forget(EntityId entity)
    {
        m_swings.Remove(entity);
        m_impacts.Remove(entity);
        m_deaths.Remove(entity);
        m_skills.Remove(entity);
        EndCastsOf(entity);
        m_pending.RemoveAll(hit => hit.Target == entity);
    }

    private readonly struct Moment
    {
        public Moment(double seconds, bool isOnLocalClock)
        {
            Seconds = seconds;
            IsOnLocalClock = isOnLocalClock;
        }

        public double Seconds { get; }

        public bool IsOnLocalClock { get; }

        public double Since(double localNow, double remoteNow)
        {
            return (IsOnLocalClock ? localNow : remoteNow) - Seconds;
        }
    }

    private readonly struct Cast
    {
        public Cast(Moment start, double seconds, EntityId target)
        {
            Start = start;
            Seconds = seconds;
            Target = target;
        }

        public Moment Start { get; }

        public double Seconds { get; }

        public EntityId Target { get; }
    }

    private readonly struct SkillUse
    {
        public SkillUse(Moment start, double castSeconds, SkillDefinitionId skill)
        {
            Start = start;
            CastSeconds = castSeconds;
            Skill = skill;
        }

        public Moment Start { get; }

        public double CastSeconds { get; }

        public SkillDefinitionId Skill { get; }
    }

    private readonly struct Swing
    {
        public Swing(Moment start, AttackTiming timing)
        {
            Start = start;
            Timing = timing;
        }

        public Moment Start { get; }

        public AttackTiming Timing { get; }
    }
}

/// <summary>
///     One hit, miss, critical, or heal to show over its target when its moment comes.
/// </summary>
public readonly struct HitMark
{
    public HitMark(
        EntityId target,
        double seconds,
        bool isOnLocalClock,
        CombatResult result,
        uint amount,
        ushort targetHealthPermille,
        bool isHeal = false)
    {
        Target = target;
        Seconds = seconds;
        IsOnLocalClock = isOnLocalClock;
        Result = result;
        Amount = amount;
        TargetHealthPermille = targetHealthPermille;
        IsHeal = isHeal;
    }

    public EntityId Target { get; }

    public double Seconds { get; }

    public bool IsOnLocalClock { get; }

    public CombatResult Result { get; }

    public uint Amount { get; }

    public ushort TargetHealthPermille { get; }

    /// <summary>
    ///     HP restored rather than lost: no squash, and a green number.
    /// </summary>
    public bool IsHeal { get; }
}
}
