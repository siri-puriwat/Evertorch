using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Resolves basic attacks (Gameplay Systems §6, §7). Each tick it first resolves the impacts that are due, then
///     begins the swings that may begin. Timing is kept in exact milliseconds: an event runs on the first tick at or
///     after its time, and a continuing schedule adds the interval to the previous nominal start, so tick alignment
///     never accumulates. Crash-fast by design (System Architecture §7).
/// </summary>
public sealed class CombatSystem : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly WorldSimulation m_world;
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly Targeting m_targeting;
    private readonly ItemDropSystem m_drops;
    private readonly CharacterProgression m_progression;
    private readonly ICombatRules m_rules;
    private readonly IRandomSource m_random;
    private readonly int m_tickRate;
    private readonly float m_rangeTolerance;
    private readonly List<WorldEntity> m_due = new();
    private readonly List<WorldEntity> m_attackers = new();

    public CombatSystem(
        WorldSimulation world,
        SessionRegistry sessions,
        MessageSender sender,
        Targeting targeting,
        ItemDropSystem drops,
        CharacterProgression progression,
        ICombatRules rules,
        IRandomSource random,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_sessions = sessions;
        m_sender = sender;
        m_targeting = targeting;
        m_drops = drops;
        m_progression = progression;
        m_rules = rules;
        m_random = random;
        m_tickRate = simulation.Value.TickRate;
        m_rangeTolerance = worldOptions.Value.AttackRangeTolerance;
    }

    public TickPhase Phase => TickPhase.Combat;

    public void Execute(in TickContext context)
    {
        long now = TickMilliseconds(context.Tick);
        foreach (MapInstance map in m_world.Maps)
        {
            ResolveImpacts(map, now, context.Tick);
            BeginSwings(map, now, context.Tick);
            foreach (WorldEntity entity in map.Entities)
            {
                entity.Combat.HasMovedThisTick = false;
            }
        }
    }

    /// <summary>
    ///     Ends <paramref name="entity" />'s life: its own and its attackers' auto-attacks end, swings at it are
    ///     interrupted, and every client that knows it hears of the death.
    /// </summary>
    public void Kill(MapInstance map, WorldEntity entity, WorldEntity? source, uint tick)
    {
        if (entity.IsDead)
        {
            return;
        }

        entity.CurrentHealth = 0;
        entity.StateFlags = (entity.StateFlags | EntityStateFlags.Dead) & ~EntityStateFlags.Moving;
        entity.VelocityX = 0f;
        entity.VelocityY = 0f;
        entity.VelocityZ = 0f;
        entity.Combat.IsAutoAttacking = false;
        entity.Combat.EndSwing();
        ClearTarget(entity);

        foreach (WorldEntity other in map.Entities)
        {
            if (other.Target == entity.Id)
            {
                ClearTarget(other);
            }

            if (other.Combat.IsSwinging && other.Combat.SwingTarget == entity.Id)
            {
                other.Combat.EndSwing();
            }
        }

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(entity.Id))
            {
                EntityId known = source != null && session.Knows(source.Id) ? source.Id : default;
                m_sender.Send(session.Connection, new EntityDied(entity.Id, known, tick));
            }
        }

        if (entity is MonsterEntity monster)
        {
            m_progression.AwardKill(map, monster);
            CharacterId killer = source is PlayerEntity player ? player.Character : default;
            m_drops.DropLoot(map, monster, killer, tick);
        }
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static CombatResult ToCombatResult(HitOutcome outcome)
    {
        switch (outcome)
        {
            case HitOutcome.Hit:
                return CombatResult.Hit;
            case HitOutcome.Critical:
                return CombatResult.Critical;
            default:
                return CombatResult.Miss;
        }
    }

    private void ResolveImpacts(MapInstance map, long now, uint tick)
    {
        m_due.Clear();
        foreach (WorldEntity entity in map.Entities)
        {
            if (entity.Combat.IsSwinging && entity.Combat.ImpactMs <= now)
            {
                m_due.Add(entity);
            }
        }

        // Several impacts in one tick resolve in a fixed order, so an attacker killed by an earlier one is
        // interrupted the same way on every run.
        m_due.Sort((left, right) =>
        {
            int byTime = left.Combat.ImpactMs.CompareTo(right.Combat.ImpactMs);
            return byTime != 0 ? byTime : left.Id.Value.CompareTo(right.Id.Value);
        });

        foreach (WorldEntity attacker in m_due)
        {
            if (!attacker.Combat.IsSwinging)
            {
                continue;
            }

            EntityId targetId = attacker.Combat.SwingTarget;
            attacker.Combat.EndSwing();
            if (attacker.IsDead
                || !map.TryGetEntity(targetId, out WorldEntity? target)
                || target == null
                || target.IsDead)
            {
                continue;
            }

            Resolve(map, attacker, target, tick);
        }
    }

    private void Resolve(MapInstance map, WorldEntity attacker, WorldEntity target, uint tick)
    {
        HitResult hit = m_rules.CalculateHit(CreateHitContext(attacker, target));
        int amount = 0;
        CombatResult result = ToCombatResult(hit.Outcome);
        if (hit.DealsDamage)
        {
            DamageResult damage = m_rules.CalculateDamage(
                CreateDamageContext(attacker, target, hit.Outcome == HitOutcome.Critical));
            amount = damage.Amount;
            target.CurrentHealth = Math.Max(0, target.CurrentHealth - amount);
        }

        if (target is MonsterEntity damaged && attacker is PlayerEntity damager && amount > 0)
        {
            // The whole roll counts toward the experience share, a killing blow's overkill included.
            damaged.LogDamage(damager.Character, amount);
        }

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(target.Id))
            {
                EntityId source = session.Knows(attacker.Id) ? attacker.Id : default;
                m_sender.Send(
                    session.Connection,
                    new Damage(source, target.Id, result, (uint)amount, tick, target.SharedHealthPermille));
            }
        }

        if (target is MonsterEntity monster && amount > 0 && monster.Brain.State != MonsterAiState.ReturnHome)
        {
            // A passive monster turns on whoever damages it; while it walks home it ignores damage.
            monster.Brain.LastAttacker = attacker.Id;
        }

        if (target is PlayerEntity player && amount > 0 && player.Owner != default)
        {
            m_sender.Send(player.Owner, new CharacterHealth((uint)player.CurrentHealth, (uint)player.MaxHealth));
        }

        if (target.CurrentHealth == 0)
        {
            Kill(map, target, attacker, tick);
        }
    }

    private HitContext CreateHitContext(WorldEntity attacker, WorldEntity target)
    {
        switch (attacker)
        {
            case PlayerEntity player when target is MonsterEntity monster:
                return new HitContext(
                    player.Stats.Hit,
                    player.Stats.Critical,
                    monster.Definition.Flee,
                    0,
                    0,
                    m_random);
            case MonsterEntity monster when target is PlayerEntity player:
                return new HitContext(
                    monster.Definition.Hit,
                    0,
                    player.Stats.Flee,
                    player.Stats.PerfectDodge,
                    player.Primary.Luk,
                    m_random);
            default:
                throw new InvalidOperationException(
                    $"No basic attack is defined from {attacker.Kind} {attacker.Id} to {target.Kind} {target.Id}.");
        }
    }

    private DamageContext CreateDamageContext(WorldEntity attacker, WorldEntity target, bool isCritical)
    {
        // Evertorch monsters have no primary statistics, so no soft defense, and players wear no armour yet, so no
        // hard defense (research note, intentional differences).
        switch (attacker)
        {
            case PlayerEntity player when target is MonsterEntity monster:
                return new DamageContext(
                    AttackerKind.Character,
                    player.Stats.PhysicalAttack,
                    0,
                    monster.Definition.PhysicalDefense,
                    0,
                    isCritical,
                    m_random);
            case MonsterEntity monster when target is PlayerEntity player:
                return new DamageContext(
                    AttackerKind.Monster,
                    0,
                    monster.Definition.PhysicalAttack,
                    0,
                    player.Stats.SoftDefense,
                    isCritical,
                    m_random);
            default:
                throw new InvalidOperationException(
                    $"No basic attack is defined from {attacker.Kind} {attacker.Id} to {target.Kind} {target.Id}.");
        }
    }

    private void BeginSwings(MapInstance map, long now, uint tick)
    {
        m_attackers.Clear();
        foreach (WorldEntity entity in map.Entities)
        {
            if (entity.Combat.IsAutoAttacking && !entity.Combat.IsSwinging && !entity.IsDead)
            {
                m_attackers.Add(entity);
            }
        }

        foreach (WorldEntity attacker in m_attackers)
        {
            if (!TryGetValidTarget(map, attacker, out WorldEntity? target) || target == null)
            {
                ClearTarget(attacker);
                continue;
            }

            if (attacker.Combat.HasMovedThisTick
                || attacker.Combat.NextNominalMs > now
                || !IsInRange(attacker, target)
                || !map.Definition.Navigation.HasLineOfSight(attacker.Position, target.Position))
            {
                continue;
            }

            Begin(map, attacker, target, now, tick);
        }
    }

    private bool TryGetValidTarget(MapInstance map, WorldEntity attacker, out WorldEntity? target)
    {
        target = null;
        if (attacker is PlayerEntity player)
        {
            ClientSession? session = FindSession(player);
            return session != null
                && Targeting.IsTargetable(session, player.Target)
                && map.TryGetEntity(player.Target, out target);
        }

        return map.TryGetEntity(attacker.Target, out target) && target != null && !target.IsDead;
    }

    private bool IsInRange(WorldEntity attacker, WorldEntity target)
    {
        float reach = attacker.AttackRange + (attacker is PlayerEntity ? m_rangeTolerance : 0f);
        return HorizontalDistance(attacker.Position, target.Position) <= reach;
    }

    private void Begin(MapInstance map, WorldEntity attacker, WorldEntity target, long now, uint tick)
    {
        AttackTiming timing = m_rules.CalculateAttackTiming(CreateAttackContext(attacker));

        // The schedule carries on only when this swing begins on the first tick at or after its nominal start;
        // any later start, because the attacker was walking or the target was out of reach, begins a new one.
        long next = attacker.Combat.NextNominalMs;
        bool isCarried = next != long.MinValue && tick > 1 && now >= next && TickMilliseconds(tick - 1) < next;
        long nominal = isCarried ? next : now;

        attacker.Combat.BeginSwing(target.Id, tick, nominal, timing);
        WorldDirection facing = MovementModel.NormalizeOrZero(
            target.Position.X - attacker.Position.X,
            target.Position.Z - attacker.Position.Z);
        if (facing != default)
        {
            attacker.Facing = facing;
        }

        foreach (ClientSession session in SessionsOn(map))
        {
            if (session.Knows(attacker.Id))
            {
                EntityId known = session.Knows(target.Id) ? target.Id : default;
                m_sender.Send(session.Connection, new AttackStarted(attacker.Id, known, tick, timing));
            }
        }
    }

    private AttackContext CreateAttackContext(WorldEntity attacker)
    {
        switch (attacker)
        {
            case PlayerEntity player:
                return AttackContext.ForAttackSpeed(player.Stats.AttackSpeed);
            case MonsterEntity monster:
                return AttackContext.ForFixedInterval(TimeSpan.FromMilliseconds(monster.Definition.AttackIntervalMs));
            default:
                throw new InvalidOperationException($"{attacker.Kind} {attacker.Id} cannot attack.");
        }
    }

    private void ClearTarget(WorldEntity entity)
    {
        entity.Combat.IsAutoAttacking = false;
        if (entity is PlayerEntity player)
        {
            ClientSession? session = FindSession(player);
            if (session != null)
            {
                m_targeting.ClearIfTargeting(session, player.Target);
                return;
            }
        }

        entity.Target = default;
    }

    private ClientSession? FindSession(PlayerEntity player)
    {
        return m_sessions.TryGet(player.Owner, out ClientSession? session)
            && session != null
            && session.Player == player
                ? session
                : null;
    }

    private IEnumerable<ClientSession> SessionsOn(MapInstance map)
    {
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Map == map)
            {
                yield return session;
            }
        }
    }

    private long TickMilliseconds(uint tick)
    {
        return (long)(tick - 1) * MillisecondsPerSecond / m_tickRate;
    }
}
}
