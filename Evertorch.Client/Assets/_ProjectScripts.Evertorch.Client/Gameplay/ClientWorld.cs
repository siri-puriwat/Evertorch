using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's copy of the map instance it entered: the predicted local player and the remote entities the server
///     has announced. It only ever follows the server; nothing here decides an outcome.
/// </summary>
public sealed class ClientWorld
{
    private readonly Dictionary<EntityId, RemoteEntity> m_remotes = new();
    private readonly Dictionary<EntityId, NpcServices> m_npcServices = new();

    // The tick of each entity's latest death or revival. Snapshots are unreliable and the events reliable, so a
    // snapshot from before either can still arrive afterwards; it must neither raise a corpse nor put a revived body
    // back where it died.
    private readonly Dictionary<EntityId, uint> m_lifeChangeTicks = new();
    private readonly double m_tickSeconds;
    private EntityId m_localCastTarget;

    public ClientWorld(NavigationGrid grid, WorldEntered entered, uint serverTickRate)
    {
        if (entered == null)
        {
            throw new ArgumentNullException(nameof(entered));
        }

        if (serverTickRate == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverTickRate));
        }

        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Map = entered.Map;
        MapInstance = entered.MapInstance;
        LocalEntity = entered.LocalEntity;
        LocalJob = entered.Job;
        Character = entered.Character;
        LocalHealth = entered.CurrentHealth;
        LocalMaximumHealth = entered.MaximumHealth;
        LocalSpirit = entered.CurrentSpirit;
        LocalMaximumSpirit = entered.MaximumSpirit;
        Level = entered.Level;
        Experience = entered.Experience;
        ExperienceToNextLevel = entered.ExperienceToNextLevel;

        // A reconnect to a character that died during its grace period enters it dead (Gameplay Systems §10.1).
        IsLocalDead = entered.CurrentHealth == 0;
        AttackRange = entered.AttackRange;
        LatestServerTick = entered.ServerTick;
        m_tickSeconds = 1.0 / serverTickRate;
        Predictor = new MovementPredictor(
            grid,
            entered.MovementSpeed,
            (float)m_tickSeconds,
            entered.Position,
            entered.Facing);
        Smoother = new RenderSmoother(entered.Position);
        ServerTime.Observe(entered.ServerTick * m_tickSeconds);
    }

    public NavigationGrid Grid { get; }

    public MapDefinitionId Map { get; }

    public uint MapInstance { get; }

    public EntityId LocalEntity { get; }

    public JobDefinitionId LocalJob { get; }

    /// <summary>
    ///     The character the server entered, which may not be the one asked for last (Network Protocol §5).
    /// </summary>
    public CharacterId Character { get; }

    /// <summary>
    ///     The local character's exact HP as the server last reported it.
    /// </summary>
    public uint LocalHealth { get; private set; }

    public uint LocalMaximumHealth { get; private set; }

    public uint LocalSpirit { get; private set; }

    public uint LocalMaximumSpirit { get; private set; }

    public ushort Level { get; private set; }

    /// <summary>
    ///     Experience toward the next level.
    /// </summary>
    public ulong Experience { get; private set; }

    /// <summary>
    ///     What the next level needs in all; 0 at the level cap.
    /// </summary>
    public ulong ExperienceToNextLevel { get; private set; }

    public bool IsLocalDead { get; private set; }

    /// <summary>
    ///     The basic attack's range, which the client walks within before asking for nothing more: the server checks
    ///     range itself when a swing begins.
    /// </summary>
    public float AttackRange { get; }

    public MovementPredictor Predictor { get; }

    public RenderSmoother Smoother { get; }

    public ServerTimeEstimator ServerTime { get; } = new();

    public ClientInventory Inventory { get; } = new();

    /// <summary>
    ///     The skills the server says the local character knows, with the cooldown left when the list was sent.
    /// </summary>
    public IReadOnlyList<SkillListEntry> Skills { get; private set; } = Array.Empty<SkillListEntry>();

    /// <summary>
    ///     The estimated server time, in seconds, when the skill list arrived; its cooldowns count down from then.
    /// </summary>
    public double SkillsReceivedAt { get; private set; }

    /// <summary>
    ///     The status effects on the local character, with the time each had left when the server sent them.
    /// </summary>
    public IReadOnlyList<StatusEffectEntry> StatusEffects { get; private set; } = Array.Empty<StatusEffectEntry>();

    /// <summary>
    ///     The local character's quests as the server last listed them, active and completed, ordered by quest ID.
    /// </summary>
    public IReadOnlyList<QuestLogEntry> Quests { get; private set; } = Array.Empty<QuestLogEntry>();

    /// <summary>
    ///     The estimated server time, in seconds, when the status effects arrived; their times count down from then.
    /// </summary>
    public double StatusEffectsReceivedAt { get; private set; }

    /// <summary>
    ///     The local player's movement lock; a new world starts unlocked.
    /// </summary>
    public ActionLock ActionLock { get; } = new();

    public IReadOnlyDictionary<EntityId, RemoteEntity> Remotes => m_remotes;

    public uint LatestServerTick { get; private set; }

    public int SnapshotsApplied { get; private set; }

    public int StaleSnapshots { get; private set; }

    public int UnknownEntityStates { get; private set; }

    /// <summary>
    ///     Entity states from snapshots older than that entity's latest death or revival; they are ignored.
    /// </summary>
    public int SupersededStates { get; private set; }

    /// <summary>
    ///     Reliable events that named an entity this client has no spawn for; they are ignored.
    /// </summary>
    public int UnknownEntityEvents { get; private set; }

    /// <summary>
    ///     The local player's target as the server last confirmed it; the default value means none.
    /// </summary>
    public EntityId Target { get; private set; }

    public double RemoteRenderTime => ServerTime.Now - RemoteEntityBuffer.InterpolationDelaySeconds;

    /// <summary>
    ///     Why the last refused command was refused, for feedback; <see cref="CommandRejectionReason.None" /> before any.
    /// </summary>
    public CommandRejectionReason LastRejection { get; private set; }

    public event Action<RemoteEntity>? RemoteSpawned;

    public event Action<RemoteEntity>? RemoteDespawned;

    public event Action? TargetChanged;

    public event Action<AttackStarted>? AttackStartedReceived;

    public event Action<Damage>? DamageReceived;

    public event Action<EntityDied>? EntityDiedReceived;

    public event Action<EntityRevived>? EntityRevivedReceived;

    public event Action<ItemDropped>? ItemDroppedReceived;

    public event Action<SkillCastStarted>? SkillCastStartedReceived;

    public event Action<SkillResolved>? SkillResolvedReceived;

    public event Action? SkillsChanged;

    public event Action? StatusEffectsChanged;

    /// <summary>
    ///     Raised after each <c>QuestLog</c>: a quest was accepted, advanced, or completed, or a baseline arrived.
    /// </summary>
    public event Action? QuestsChanged;

    /// <summary>
    ///     The local player's own cast ended before its time: it died, its target died or left, or it cancelled.
    /// </summary>
    public event Action? LocalCastEnded;

    /// <summary>
    ///     The local character reached a higher level.
    /// </summary>
    public event Action? LeveledUp;

    /// <summary>
    ///     A drop this client knows was picked up; its despawn follows.
    /// </summary>
    public event Action<ItemPickedUp>? ItemPickedUpReceived;

    /// <summary>
    ///     A command of this client was refused; <see cref="CommandRejected.CommandSequence" /> says which.
    /// </summary>
    public event Action<CommandRejected>? CommandRejectedReceived;

    /// <summary>
    ///     What an NPC this client sees offers; each spawn of an NPC is followed by its services.
    /// </summary>
    public event Action<NpcServices>? NpcServicesReceived;

    public void OnSpawn(EntitySpawn spawn)
    {
        if (spawn == null)
        {
            throw new ArgumentNullException(nameof(spawn));
        }

        if (spawn.Entity == LocalEntity)
        {
            return;
        }

        if (m_remotes.TryGetValue(spawn.Entity, out RemoteEntity? replaced))
        {
            m_remotes.Remove(spawn.Entity);
            m_npcServices.Remove(spawn.Entity);
            RemoteDespawned?.Invoke(replaced);
        }

        var remote = new RemoteEntity(
            spawn.Entity,
            spawn.Kind,
            spawn.DefinitionId,
            spawn.StateFlags,
            spawn.HealthPermille);
        remote.Buffer.Add(LatestServerTick * m_tickSeconds, spawn.Position, spawn.Facing);
        m_remotes.Add(spawn.Entity, remote);
        RemoteSpawned?.Invoke(remote);
    }

    public void OnDespawn(EntityDespawn despawn)
    {
        if (m_remotes.TryGetValue(despawn.Entity, out RemoteEntity? remote))
        {
            m_remotes.Remove(despawn.Entity);
            m_npcServices.Remove(despawn.Entity);
            EndLocalCastAt(despawn.Entity);
            RemoteDespawned?.Invoke(remote);
        }
    }

    /// <summary>
    ///     A cast began. The local player's own cast holds it still for the cast time from now: the server held it a
    ///     little earlier, from the tick it began (Gameplay Systems §5.1).
    /// </summary>
    public void OnSkillCastStarted(SkillCastStarted started)
    {
        if (!Knows(started.Caster))
        {
            UnknownEntityEvents++;
            return;
        }

        if (started.Caster == LocalEntity)
        {
            m_localCastTarget = started.Target;
            ActionLock.LockForCast((int)Math.Ceiling(started.CastMs / 1000.0 / m_tickSeconds));
        }

        SkillCastStartedReceived?.Invoke(started);
    }

    public void OnSkillResolved(SkillResolved resolved)
    {
        if (!Knows(resolved.Target))
        {
            UnknownEntityEvents++;
            return;
        }

        if (m_remotes.TryGetValue(resolved.Target, out RemoteEntity? remote) && remote.Kind == EntityKind.Monster)
        {
            remote.HealthPermille = resolved.TargetHealthPermille;
        }

        SkillResolvedReceived?.Invoke(resolved);
    }

    public void OnSkillList(SkillList list)
    {
        Skills = list.Skills;
        SkillsReceivedAt = ServerTime.Now;
        SkillsChanged?.Invoke();
    }

    /// <summary>
    ///     Keeps what an NPC offers until it despawns. Services for an entity this client has no NPC spawn for are
    ///     counted and ignored.
    /// </summary>
    public void OnNpcServices(NpcServices services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (!m_remotes.TryGetValue(services.Npc, out RemoteEntity? remote) || remote.Kind != EntityKind.Npc)
        {
            UnknownEntityEvents++;
            return;
        }

        m_npcServices[services.Npc] = services;
        NpcServicesReceived?.Invoke(services);
    }

    public bool TryGetNpcServices(EntityId npc, out NpcServices? services)
    {
        return m_npcServices.TryGetValue(npc, out services);
    }

    public void OnQuestLog(QuestLog log)
    {
        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        Quests = log.Entries;
        QuestsChanged?.Invoke();
    }

    public void OnStatusEffects(StatusEffects effects)
    {
        StatusEffects = effects.Effects;
        StatusEffectsReceivedAt = ServerTime.Now;
        StatusEffectsChanged?.Invoke();
    }

    /// <summary>
    ///     What is left of a status effect on the local character now, in seconds; 0 once it has ended by its time,
    ///     and for one it does not have.
    /// </summary>
    public double StatusRemaining(StatusDefinitionId status)
    {
        foreach (StatusEffectEntry effect in StatusEffects)
        {
            if (effect.Status == status)
            {
                return Math.Max(0.0, effect.RemainingMs / 1000.0 - (ServerTime.Now - StatusEffectsReceivedAt));
            }
        }

        return 0.0;
    }

    /// <summary>
    ///     What is left of a listed skill's cooldown now, in seconds; 0 once it is ready, and for a skill not listed.
    /// </summary>
    public double CooldownRemaining(SkillDefinitionId skill)
    {
        foreach (SkillListEntry entry in Skills)
        {
            if (entry.Skill == skill)
            {
                return Math.Max(0.0, entry.RemainingCooldownMs / 1000.0 - (ServerTime.Now - SkillsReceivedAt));
            }
        }

        return 0.0;
    }

    /// <summary>
    ///     The local player asked to stop: its own cast ends at once, as the server ends it.
    /// </summary>
    public void OnLocalCancel()
    {
        EndLocalCast();
    }

    // No message says a cast was interrupted: the local cast lock ends when its target dies or leaves view.
    private void EndLocalCastAt(EntityId entity)
    {
        if (m_localCastTarget == entity && entity != default)
        {
            EndLocalCast();
        }
    }

    private void EndLocalCast()
    {
        if (ActionLock.IsCastLocked)
        {
            ActionLock.EndCast();
            LocalCastEnded?.Invoke();
        }
    }

    public void OnTargetChanged(TargetChanged changed)
    {
        if (changed.Actor != LocalEntity || (changed.Target != default && !m_remotes.ContainsKey(changed.Target)))
        {
            UnknownEntityEvents++;
            return;
        }

        Target = changed.Target;
        TargetChanged?.Invoke();
    }

    public void OnAttackStarted(AttackStarted started)
    {
        if (!Knows(started.Attacker))
        {
            UnknownEntityEvents++;
            return;
        }

        AttackStartedReceived?.Invoke(started);
    }

    public void OnDamage(Damage damage)
    {
        if (!Knows(damage.Target))
        {
            UnknownEntityEvents++;
            return;
        }

        if (m_remotes.TryGetValue(damage.Target, out RemoteEntity? remote) && remote.Kind == EntityKind.Monster)
        {
            remote.HealthPermille = damage.TargetHealthPermille;
        }

        DamageReceived?.Invoke(damage);
    }

    public void OnEntityDied(EntityDied died)
    {
        if (died.Entity == LocalEntity)
        {
            IsLocalDead = true;
            EndLocalCast();
        }
        else if (m_remotes.TryGetValue(died.Entity, out RemoteEntity? remote))
        {
            remote.StateFlags |= EntityStateFlags.Dead;
            remote.HealthPermille = 0;
        }
        else
        {
            UnknownEntityEvents++;
            return;
        }

        m_lifeChangeTicks[died.Entity] = died.ServerTick;
        EndLocalCastAt(died.Entity);

        EntityDiedReceived?.Invoke(died);
    }

    public void OnEntityRevived(EntityRevived revived)
    {
        if (revived.Entity == LocalEntity)
        {
            IsLocalDead = false;
            Predictor.Teleport(revived.Position, revived.Facing);
            Smoother.Teleport(revived.Position);
        }
        else if (m_remotes.TryGetValue(revived.Entity, out RemoteEntity? remote))
        {
            remote.StateFlags &= ~EntityStateFlags.Dead;
            remote.Buffer.Clear();
            remote.Buffer.Add(revived.ServerTick * m_tickSeconds, revived.Position, revived.Facing);
        }
        else
        {
            UnknownEntityEvents++;
            return;
        }

        m_lifeChangeTicks[revived.Entity] = revived.ServerTick;
        EntityRevivedReceived?.Invoke(revived);
    }

    /// <summary>
    ///     A drop this client saw land; its spawn came just before. Clients that meet the drop later never hear this.
    /// </summary>
    public void OnCommandRejected(CommandRejected rejected)
    {
        LastRejection = rejected.Reason;
        CommandRejectedReceived?.Invoke(rejected);
    }

    public void OnItemDropped(ItemDropped dropped)
    {
        if (dropped == null)
        {
            throw new ArgumentNullException(nameof(dropped));
        }

        if (!m_remotes.TryGetValue(dropped.Entity, out RemoteEntity? remote) || remote.Kind != EntityKind.ItemDrop)
        {
            UnknownEntityEvents++;
            return;
        }

        ItemDroppedReceived?.Invoke(dropped);
    }

    public void OnItemPickedUp(ItemPickedUp pickedUp)
    {
        if (pickedUp == null)
        {
            throw new ArgumentNullException(nameof(pickedUp));
        }

        if (!m_remotes.TryGetValue(pickedUp.Drop, out RemoteEntity? remote) || remote.Kind != EntityKind.ItemDrop)
        {
            UnknownEntityEvents++;
            return;
        }

        ItemPickedUpReceived?.Invoke(pickedUp);
    }

    public void OnCharacterHealth(CharacterHealth health)
    {
        LocalHealth = health.Current;
        LocalMaximumHealth = health.Maximum;
        LocalSpirit = health.CurrentSpirit;
        LocalMaximumSpirit = health.MaximumSpirit;
    }

    public void OnCharacterProgress(CharacterProgress progress)
    {
        bool isLevelUp = progress.Level > Level;
        Level = progress.Level;
        Experience = progress.Experience;
        ExperienceToNextLevel = progress.ExperienceToNextLevel;
        if (isLevelUp)
        {
            LeveledUp?.Invoke();
        }
    }

    /// <summary>
    ///     Appends the live monsters this client knows, at their drawn positions: what a click can pick and what
    ///     target cycling visits.
    /// </summary>
    public void CollectTargetCandidates(List<PickCandidate> candidates)
    {
        double renderTime = RemoteRenderTime;
        foreach (RemoteEntity remote in m_remotes.Values)
        {
            if (remote.Kind == EntityKind.Monster
                && !remote.IsDead
                && remote.Buffer.TrySample(renderTime, out WorldPosition position, out WorldDirection _))
            {
                candidates.Add(new PickCandidate(remote.Entity, position));
            }
        }
    }

    /// <summary>
    ///     Appends the drops this client knows, at their drawn positions: what a click or tap picks up.
    /// </summary>
    public void CollectDropCandidates(List<PickCandidate> candidates)
    {
        double renderTime = RemoteRenderTime;
        foreach (RemoteEntity remote in m_remotes.Values)
        {
            if (remote.Kind == EntityKind.ItemDrop
                && remote.Buffer.TrySample(renderTime, out WorldPosition position, out WorldDirection _))
            {
                candidates.Add(new PickCandidate(remote.Entity, position));
            }
        }
    }

    /// <summary>
    ///     Appends the NPCs this client knows, at their drawn positions: what a click or tap talks to.
    /// </summary>
    public void CollectNpcCandidates(List<PickCandidate> candidates)
    {
        double renderTime = RemoteRenderTime;
        foreach (RemoteEntity remote in m_remotes.Values)
        {
            if (remote.Kind == EntityKind.Npc
                && remote.Buffer.TrySample(renderTime, out WorldPosition position, out WorldDirection _))
            {
                candidates.Add(new PickCandidate(remote.Entity, position));
            }
        }
    }

    /// <summary>
    ///     The drawn NPC nearest to <paramref name="position" />, or default when this client sees none: what the talk
    ///     control talks to (Prototype Content §4).
    /// </summary>
    public EntityId NearestNpc(WorldPosition position)
    {
        EntityId nearest = default;
        float best = float.MaxValue;
        double renderTime = RemoteRenderTime;
        foreach (RemoteEntity remote in m_remotes.Values)
        {
            if (remote.Kind != EntityKind.Npc
                || !remote.Buffer.TrySample(renderTime, out WorldPosition at, out WorldDirection _))
            {
                continue;
            }

            float dx = at.X - position.X;
            float dz = at.Z - position.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < best || (distance == best && remote.Entity.Value < nearest.Value))
            {
                best = distance;
                nearest = remote.Entity;
            }
        }

        return nearest;
    }

    /// <summary>
    ///     The drawn drop nearest to <paramref name="position" /> within <paramref name="reach" />, or default.
    /// </summary>
    public EntityId NearestDrop(WorldPosition position, float reach)
    {
        EntityId nearest = default;
        float best = reach;
        double renderTime = RemoteRenderTime;
        foreach (RemoteEntity remote in m_remotes.Values)
        {
            if (remote.Kind != EntityKind.ItemDrop
                || !remote.Buffer.TrySample(renderTime, out WorldPosition at, out WorldDirection _))
            {
                continue;
            }

            float dx = at.X - position.X;
            float dz = at.Z - position.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < best || (distance == best && nearest != default && remote.Entity.Value < nearest.Value))
            {
                best = distance;
                nearest = remote.Entity;
            }
        }

        return nearest;
    }

    private bool Knows(EntityId entity)
    {
        return entity == LocalEntity || m_remotes.ContainsKey(entity);
    }

    public void OnSnapshot(EntitySnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        // Snapshots are unreliable, so an older one can arrive late. A large area is split across several
        // snapshots with the same tick, which is why an equal tick is still applied.
        if (unchecked((int)(snapshot.ServerTick - LatestServerTick)) < 0)
        {
            StaleSnapshots++;
            return;
        }

        LatestServerTick = snapshot.ServerTick;
        SnapshotsApplied++;
        double serverTime = snapshot.ServerTick * m_tickSeconds;
        ServerTime.Observe(serverTime);

        foreach (EntityState state in snapshot.Entities)
        {
            if (IsFromBeforeLifeChange(state.Entity, snapshot.ServerTick))
            {
                SupersededStates++;
                continue;
            }

            if (state.Entity == LocalEntity)
            {
                WorldPosition before = Predictor.Position;
                Predictor.Reconcile(state, snapshot.LastProcessedInputSequence);
                if (IsLocalDead)
                {
                    // A dead body only moves by reviving. The snapshot of the revival tick can overtake the reliable
                    // EntityRevived, and that move is a teleport, not a correction.
                    Smoother.Teleport(Predictor.Position);
                }
                else
                {
                    Smoother.OnCorrected(before, Predictor.Position);
                }
            }
            else if (m_remotes.TryGetValue(state.Entity, out RemoteEntity? remote))
            {
                remote.StateFlags = state.StateFlags;
                remote.Buffer.Add(serverTime, state.Position, state.Facing);
            }
            else
            {
                // The spawn travels on the reliable stream and may still be on its way.
                UnknownEntityStates++;
            }
        }
    }

    private bool IsFromBeforeLifeChange(EntityId entity, uint serverTick)
    {
        if (!m_lifeChangeTicks.TryGetValue(entity, out uint changeTick))
        {
            return false;
        }

        if (unchecked((int)(serverTick - changeTick)) < 0)
        {
            return true;
        }

        m_lifeChangeTicks.Remove(entity);
        return false;
    }

    public void Advance(float deltaSeconds)
    {
        ServerTime.Advance(deltaSeconds);
        Smoother.Advance(deltaSeconds);
    }
}
}
