using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;
using UnityEngine;
using EntityId = Evertorch.Game.EntityId;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     Shows the fight the server reports: lunges, squashes, floating numbers, heals, monster health bars, cast bars,
///     and dead bodies. It listens to <see cref="ClientWorld" /> and only draws; nothing here sends a command or
///     changes what the client believes about the fight.
/// </summary>
public sealed class CombatPresenter : IDisposable
{
    // Over the body's overhead point (Prototype Content §2): 1.45 and 1.6 m over a graybox body, as before anchors.
    private const float CastBarLift = 0.25f;
    private const float NumberLift = 0.4f;
    private const float CriticalScale = 1.4f;

    private static readonly Color HitColor = new(1f, 1f, 1f);
    private static readonly Color CriticalColor = new(1f, 0.85f, 0.2f);
    private static readonly Color MissColor = new(0.7f, 0.7f, 0.75f);
    private static readonly Color LocalHitColor = new(1f, 0.35f, 0.3f);
    private static readonly Color HealColor = new(0.35f, 0.9f, 0.4f);
    private static readonly Color BarBackColor = new(0.15f, 0.05f, 0.05f);
    private static readonly Color BarFillColor = new(0.85f, 0.2f, 0.2f);
    private static readonly Color CastBackColor = new(0.05f, 0.08f, 0.2f);
    private static readonly Color CastFillColor = new(0.3f, 0.55f, 1f);

    private readonly ClientWorld m_world;
    private readonly ClientContent? m_content;
    private readonly ClientParty? m_party;
    private readonly double m_tickSeconds;
    private readonly Dictionary<EntityId, HealthBar> m_bars = new();
    private readonly Dictionary<EntityId, CastBar> m_castBars = new();
    private readonly Dictionary<EntityId, ushort> m_shownHealth = new();
    private readonly List<HitMark> m_due = new();
    private readonly Material m_barBack;
    private readonly Material m_barFill;
    private readonly Material m_castBack;
    private readonly Material m_castFill;

    public CombatPresenter(
        ClientWorld world,
        double tickSeconds,
        Material baseMaterial,
        ClientContent? content = null,
        ClientParty? party = null)
    {
        m_world = world ?? throw new ArgumentNullException(nameof(world));
        m_content = content;
        m_party = party;
        if (baseMaterial == null)
        {
            throw new ArgumentNullException(nameof(baseMaterial));
        }

        m_tickSeconds = tickSeconds;
        m_barBack = new Material(baseMaterial) { color = BarBackColor };
        m_barFill = new Material(baseMaterial) { color = BarFillColor };
        m_castBack = new Material(baseMaterial) { color = CastBackColor };
        m_castFill = new Material(baseMaterial) { color = CastFillColor };
        m_world.AttackStartedReceived += OnAttackStarted;
        m_world.DamageReceived += OnDamage;
        m_world.SkillCastStartedReceived += OnSkillCastStarted;
        m_world.SkillResolvedReceived += OnSkillResolved;
        m_world.LocalCastEnded += OnLocalCastEnded;
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

    /// <summary>
    ///     The NPC whose window the local player has open, which talks while it shows; none otherwise.
    /// </summary>
    public EntityId TalkingNpc { get; set; }

    public void Dispose()
    {
        m_world.AttackStartedReceived -= OnAttackStarted;
        m_world.DamageReceived -= OnDamage;
        m_world.SkillCastStartedReceived -= OnSkillCastStarted;
        m_world.SkillResolvedReceived -= OnSkillResolved;
        m_world.LocalCastEnded -= OnLocalCastEnded;
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
        foreach (CastBar bar in m_castBars.Values)
        {
            if (bar != null)
            {
                Object.Destroy(bar.gameObject);
            }
        }

        m_castBars.Clear();
        Object.Destroy(m_barBack);
        Object.Destroy(m_barFill);
        Object.Destroy(m_castBack);
        Object.Destroy(m_castFill);
    }

    public bool TryGetHealthBar(EntityId entity, out HealthBar? bar)
    {
        return m_bars.TryGetValue(entity, out bar);
    }

    public bool TryGetCastBar(EntityId caster, out CastBar? bar)
    {
        return m_castBars.TryGetValue(caster, out bar);
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
            bool isLocalShownDead = Timeline.IsShownDead(self, m_world.IsLocalDead, localNow, remoteNow);
            local.SetCombatPose(
                Timeline.Lunge(self, localNow, remoteNow),
                Timeline.Squash(self, localNow, remoteNow),
                isLocalShownDead);
            Animate(local, self, isLocalShownDead, localNow, remoteNow);
            PresentCastBar(self, local, localNow, remoteNow, camera);
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
            Animate(pair.Value, pair.Key, isShownDead, localNow, remoteNow);
            if (remote.Kind == EntityKind.Monster)
            {
                PresentHealthBar(remote, pair.Value, isShownDead, camera);
            }
            else if (remote.Kind == EntityKind.Player)
            {
                PresentMemberBar(remote, pair.Value, isShownDead, camera);
            }

            PresentCastBar(pair.Key, pair.Value, localNow, remoteNow, camera);
        }
    }

    private static string Describe(HitMark hit)
    {
        if (hit.IsHeal)
        {
            return "+" + hit.Amount.ToString(CultureInfo.InvariantCulture);
        }

        return hit.Result switch
        {
            CombatResult.Miss => "Miss",
            CombatResult.Critical => hit.Amount.ToString(CultureInfo.InvariantCulture) + "!",
            _ => hit.Amount.ToString(CultureInfo.InvariantCulture)
        };
    }

    // A rigged body plays its clips from the same moments the procedural pose follows (Gameplay Systems §8). The
    // skill's target type comes from the client content, since a cast's target of 0 also means one the receiver does
    // not know.
    private void Animate(EntityView view, EntityId entity, bool isShownDead, double localNow, double remoteNow)
    {
        if (!view.HasClips)
        {
            return;
        }

        var cue = new BodyCue
        {
            IsDead = isShownDead,
            DeathSince = Timeline.DeadSince(entity, localNow, remoteNow),
            AttackClip = view.AttackClip,
            IsTalking = entity != default && entity == TalkingNpc
        };
        if (Timeline.TryGetSwing(entity, localNow, remoteNow, out double swingSince, out AttackTiming timing))
        {
            cue.HasSwing = true;
            cue.SwingSince = swingSince;
            cue.Impact = timing.Impact.TotalSeconds;
        }

        if (Timeline.TryGetSkill(
                entity,
                localNow,
                remoteNow,
                out double skillSince,
                out double castSeconds,
                out SkillDefinitionId skill))
        {
            cue.HasSkill = true;
            cue.SkillSince = skillSince;
            cue.CastSeconds = castSeconds;
            cue.IsEnemySkill = true;
            if (m_content != null && m_content.TryGetSkill(skill, out ClientSkill? definition) && definition != null)
            {
                cue.IsEnemySkill = definition.TargetType == SkillTargetType.Enemy;
                cue.HasProjectile = ProjectilePresenter.TryGetCastProjectile(m_world, m_content, entity, skill, out _);
            }
        }

        if (Timeline.TryGetLastHit(entity, localNow, remoteNow, out double hitSince))
        {
            cue.HasHit = true;
            cue.HitSince = hitSince;
        }

        view.Animate(cue, Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime);
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
        bar.Show(view.OverheadPoint(0f), camera, shown);
    }

    // A party member's health reaches its party alone, so only a member's bar shows over a player (Network Protocol
    // §9); it follows the member's status, at most once a second.
    private void PresentMemberBar(RemoteEntity player, EntityView view, bool isShownDead, Camera? camera)
    {
        int? health = null;
        if (m_party != null && m_party.TryGetMember(player.Name, out PartyMember? member))
        {
            health = member!.HealthPermille;
        }

        if (!m_bars.TryGetValue(player.Entity, out HealthBar? bar))
        {
            if (health == null)
            {
                return;
            }

            bar = HealthBar.Create(m_barBack, m_barFill);
            m_bars.Add(player.Entity, bar);
        }

        if (health == null || isShownDead)
        {
            bar.Hide();
            return;
        }

        bar.Show(view.OverheadPoint(0f), camera, health.Value);
    }

    private void PresentCastBar(EntityId caster, EntityView view, double localNow, double remoteNow, Camera? camera)
    {
        bool isCasting = Timeline.TryGetCastProgress(caster, localNow, remoteNow, out float progress);
        if (!m_castBars.TryGetValue(caster, out CastBar? bar))
        {
            if (!isCasting)
            {
                return;
            }

            bar = CastBar.Create(m_castBack, m_castFill);
            m_castBars.Add(caster, bar);
        }

        if (isCasting)
        {
            bar.Show(view.OverheadPoint(CastBarLift), camera, progress);
        }
        else
        {
            bar.Hide();
        }
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

        Color color = hit.IsHeal
            ? HealColor
            : hit.Result switch
            {
                CombatResult.Miss => MissColor,
                CombatResult.Critical => CriticalColor,
                _ => isLocal ? LocalHitColor : HitColor
            };
        float scale = hit.Result == CombatResult.Critical ? CriticalScale : 1f;
        FloatingNumber.Create(Describe(hit), color, scale, view.OverheadPoint(NumberLift));
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

    // The local cast is drawn from when its message arrived, like the local swing, and a remote one on its caster's
    // interpolated timeline.
    private void OnSkillCastStarted(SkillCastStarted started)
    {
        bool isLocal = started.Caster == m_world.LocalEntity;
        double at = isLocal ? m_world.ServerTime.Now : started.StartTick * m_tickSeconds;
        Timeline.BeginCast(started.Caster, started.Target, at, started.CastMs / 1000.0, isLocal, started.Skill);
    }

    private void OnSkillResolved(SkillResolved resolved)
    {
        CombatResult result;
        switch (resolved.Outcome)
        {
            case SkillOutcome.Hit:
            case SkillOutcome.Healed:
                result = CombatResult.Hit;
                break;
            case SkillOutcome.Miss:
                result = CombatResult.Miss;
                break;
            case SkillOutcome.Critical:
                result = CombatResult.Critical;
                break;
            default:
                return;
        }

        bool isLocal = resolved.Target == m_world.LocalEntity;
        double at = isLocal ? m_world.ServerTime.Now : resolved.ServerTick * m_tickSeconds;
        Timeline.AddHit(
            new HitMark(
                resolved.Target,
                at,
                isLocal,
                result,
                resolved.Amount,
                resolved.TargetHealthPermille,
                resolved.Outcome == SkillOutcome.Healed));
    }

    private void OnLocalCastEnded()
    {
        Timeline.EndCast(m_world.LocalEntity);
    }

    private void OnEntityDied(EntityDied died)
    {
        bool isLocal = died.Entity == m_world.LocalEntity;
        double at = isLocal ? m_world.ServerTime.Now : died.ServerTick * m_tickSeconds;
        Timeline.MarkDeath(died.Entity, at, isLocal);
        Timeline.EndSwing(died.Entity);
        Timeline.EndCastsOf(died.Entity);
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

        if (m_castBars.TryGetValue(remote.Entity, out CastBar? castBar))
        {
            m_castBars.Remove(remote.Entity);
            if (castBar != null)
            {
                Object.Destroy(castBar.gameObject);
            }
        }
    }
}
}
