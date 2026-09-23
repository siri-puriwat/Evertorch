using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Protocol;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     Shows the fight the server reports: lunges, squashes, floating numbers, monster health bars, and dead bodies.
///     It listens to <see cref="ClientWorld" /> and only draws; nothing here sends a command or changes what the
///     client believes about the fight.
/// </summary>
public sealed class CombatPresenter : IDisposable
{
    private const float BarHeight = 1.2f;
    private const float NumberHeight = 1.6f;
    private const float CriticalScale = 1.4f;

    private static readonly Color HitColor = new(1f, 1f, 1f);
    private static readonly Color CriticalColor = new(1f, 0.85f, 0.2f);
    private static readonly Color MissColor = new(0.7f, 0.7f, 0.75f);
    private static readonly Color LocalHitColor = new(1f, 0.35f, 0.3f);
    private static readonly Color BarBackColor = new(0.15f, 0.05f, 0.05f);
    private static readonly Color BarFillColor = new(0.85f, 0.2f, 0.2f);

    private readonly ClientWorld m_world;
    private readonly double m_tickSeconds;
    private readonly Dictionary<EntityId, HealthBar> m_bars = new();
    private readonly Dictionary<EntityId, ushort> m_shownHealth = new();
    private readonly List<HitMark> m_due = new();
    private readonly Material m_barBack;
    private readonly Material m_barFill;

    public CombatPresenter(ClientWorld world, double tickSeconds, Material baseMaterial)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        m_tickSeconds = tickSeconds;
        m_barBack = new Material(baseMaterial) { color = BarBackColor };
        m_barFill = new Material(baseMaterial) { color = BarFillColor };
        m_world.AttackStartedReceived += OnAttackStarted;
        m_world.DamageReceived += OnDamage;
        m_world.EntityDiedReceived += OnEntityDied;
        m_world.EntityRevivedReceived += OnEntityRevived;
        m_world.RemoteSpawned += OnRemoteSpawned;
        m_world.RemoteDespawned += OnRemoteDespawned;
        foreach (RemoteEntity remote in m_world.Remotes.Values)
        {
            OnRemoteSpawned(remote);
        }
    }

    public CombatTimeline Timeline { get; } = new();

    public int NumbersShown { get; private set; }

    public void Dispose()
    {
        m_world.AttackStartedReceived -= OnAttackStarted;
        m_world.DamageReceived -= OnDamage;
        m_world.EntityDiedReceived -= OnEntityDied;
        m_world.EntityRevivedReceived -= OnEntityRevived;
        m_world.RemoteSpawned -= OnRemoteSpawned;
        m_world.RemoteDespawned -= OnRemoteDespawned;
        foreach (HealthBar bar in m_bars.Values)
        {
            if (bar != null)
            {
                Object.Destroy(bar.gameObject);
            }
        }

        m_bars.Clear();
        Object.Destroy(m_barBack);
        Object.Destroy(m_barFill);
    }

    public bool TryGetHealthBar(EntityId entity, out HealthBar? bar)
    {
        return m_bars.TryGetValue(entity, out bar);
    }

    /// <summary>
    ///     Poses every view for this frame and shows the hits whose moment has come. Call after the views were placed.
    /// </summary>
    public void Present(EntityView? local, IReadOnlyDictionary<EntityId, EntityView> remotes, Camera? camera)
    {
        double localNow = m_world.ServerTime.Now;
        double remoteNow = m_world.RemoteRenderTime;
        m_due.Clear();
        Timeline.CollectDueHits(localNow, remoteNow, m_due);
        foreach (HitMark hit in m_due)
        {
            ShowHit(hit, local, remotes);
        }

        if (local != null)
        {
            EntityId self = m_world.LocalEntity;
            local.SetCombatPose(
                Timeline.Lunge(self, localNow, remoteNow),
                Timeline.Squash(self, localNow, remoteNow),
                Timeline.IsShownDead(self, m_world.IsLocalDead, localNow, remoteNow));
        }

        foreach (KeyValuePair<EntityId, EntityView> pair in remotes)
        {
            if (!m_world.Remotes.TryGetValue(pair.Key, out RemoteEntity? remote))
            {
                continue;
            }

            bool isShownDead = Timeline.IsShownDead(pair.Key, remote.IsDead, localNow, remoteNow);
            pair.Value.SetCombatPose(
                Timeline.Lunge(pair.Key, localNow, remoteNow),
                Timeline.Squash(pair.Key, localNow, remoteNow),
                isShownDead);
            if (remote.Kind == EntityKind.Monster)
            {
                PresentHealthBar(remote, pair.Value, isShownDead, camera);
            }
        }
    }

    private static string Describe(HitMark hit)
    {
        return hit.Result switch
        {
            CombatResult.Miss => "Miss",
            CombatResult.Critical => hit.Amount.ToString(CultureInfo.InvariantCulture) + "!",
            _ => hit.Amount.ToString(CultureInfo.InvariantCulture)
        };
    }

    private void PresentHealthBar(RemoteEntity monster, EntityView view, bool isShownDead, Camera? camera)
    {
        if (!m_bars.TryGetValue(monster.Entity, out HealthBar? bar))
        {
            bar = HealthBar.Create(m_barBack, m_barFill);
            m_bars.Add(monster.Entity, bar);
        }

        if (isShownDead)
        {
            bar.Hide();
            return;
        }

        // The bar moves with the numbers, on the monster's own timeline, not when the message arrives.
        m_shownHealth.TryGetValue(monster.Entity, out ushort shown);
        bar.Show(view.transform.position + Vector3.up * BarHeight, camera, shown);
    }

    private void ShowHit(HitMark hit, EntityView? local, IReadOnlyDictionary<EntityId, EntityView> remotes)
    {
        bool isLocal = hit.Target == m_world.LocalEntity;
        if (!isLocal)
        {
            m_shownHealth[hit.Target] = hit.TargetHealthPermille;
        }

        EntityView? view = isLocal ? local : remotes.TryGetValue(hit.Target, out EntityView? found) ? found : null;
        if (view == null)
        {
            return;
        }

        Color color = hit.Result switch
        {
            CombatResult.Miss => MissColor,
            CombatResult.Critical => CriticalColor,
            _ => isLocal ? LocalHitColor : HitColor
        };
        float scale = hit.Result == CombatResult.Critical ? CriticalScale : 1f;
        FloatingNumber.Create(Describe(hit), color, scale, view.transform.position + Vector3.up * NumberHeight);
        NumbersShown++;
    }

    private void OnAttackStarted(AttackStarted started)
    {
        if (started.Attacker == m_world.LocalEntity)
        {
            // The local lock counts from hearing of the swing, so the local lunge does too.
            Timeline.BeginSwing(started.Attacker, m_world.ServerTime.Now, started.Timing, true);
        }
        else
        {
            Timeline.BeginSwing(started.Attacker, started.StartTick * m_tickSeconds, started.Timing, false);
        }
    }

    private void OnDamage(Damage damage)
    {
        bool isLocal = damage.Target == m_world.LocalEntity;
        double at = isLocal ? m_world.ServerTime.Now : damage.ServerTick * m_tickSeconds;
        Timeline.AddHit(
            new HitMark(
                damage.Target,
                at,
                isLocal,
                damage.Result,
                damage.Amount,
                damage.TargetHealthPermille));
    }

    private void OnEntityDied(EntityDied died)
    {
        bool isLocal = died.Entity == m_world.LocalEntity;
        double at = isLocal ? m_world.ServerTime.Now : died.ServerTick * m_tickSeconds;
        Timeline.MarkDeath(died.Entity, at, isLocal);
    }

    private void OnEntityRevived(EntityRevived revived)
    {
        Timeline.ClearDeath(revived.Entity);
    }

    private void OnRemoteSpawned(RemoteEntity remote)
    {
        if (remote.Kind == EntityKind.Monster)
        {
            m_shownHealth[remote.Entity] = remote.HealthPermille;
        }
    }

    private void OnRemoteDespawned(RemoteEntity remote)
    {
        Timeline.Forget(remote.Entity);
        m_shownHealth.Remove(remote.Entity);
        if (m_bars.TryGetValue(remote.Entity, out HealthBar? bar))
        {
            m_bars.Remove(remote.Entity);
            if (bar != null)
            {
                Object.Destroy(bar.gameObject);
            }
        }
    }
}
}
