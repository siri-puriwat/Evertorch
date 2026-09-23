using System.Collections.Generic;
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
    private readonly List<HitMark> m_pending = new();

    public int PendingHits => m_pending.Count;

    public void BeginSwing(EntityId attacker, double startSeconds, AttackTiming timing, bool isOnLocalClock)
    {
        m_swings[attacker] = new Swing(new Moment(startSeconds, isOnLocalClock), timing);
    }

    public void AddHit(HitMark hit)
    {
        m_pending.Add(hit);
        if (hit.Result != CombatResult.Miss)
        {
            m_impacts[hit.Target] = new Moment(hit.Seconds, hit.IsOnLocalClock);
        }
    }

    public void MarkDeath(EntityId entity, double atSeconds, bool isOnLocalClock)
    {
        m_deaths[entity] = new Moment(atSeconds, isOnLocalClock);
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
///     One hit, miss, or critical to show over its target when its moment comes.
/// </summary>
public readonly struct HitMark
{
    public HitMark(
        EntityId target,
        double seconds,
        bool isOnLocalClock,
        CombatResult result,
        uint amount,
        ushort targetHealthPermille)
    {
        Target = target;
        Seconds = seconds;
        IsOnLocalClock = isOnLocalClock;
        Result = result;
        Amount = amount;
        TargetHealthPermille = targetHealthPermille;
    }

    public EntityId Target { get; }

    public double Seconds { get; }

    public bool IsOnLocalClock { get; }

    public CombatResult Result { get; }

    public uint Amount { get; }

    public ushort TargetHealthPermille { get; }
}
}
