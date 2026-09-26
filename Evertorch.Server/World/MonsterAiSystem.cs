using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Runs every monster's AI in the <c>MonsterAi</c> phase (Gameplay Systems §10, monster AI research note).
///     Decisions (acquiring, scanning, re-pathing, the leash) run on each monster's scan cadence; steering runs every
///     tick. The AI only chooses a direction: the movement phase of the next tick steps the monster, so "moved this
///     tick" means the same for monsters as for players. Crash-fast by design (System Architecture §7).
/// </summary>
public sealed class MonsterAiSystem : ITickPhase
{
    public const float HomeArrivalDistance = 0.5f;
    public const float ChaseRepathDistance = 1f;
    public const int MinimumRespawnMs = 1000;

    private const int MillisecondsPerSecond = 1000;
    private const int PathNodeBudget = 8192;

    private readonly WorldSimulation m_world;
    private readonly IRandomSource m_random;
    private readonly ServerInstruments m_instruments;
    private readonly int m_tickRate;
    private readonly int m_corpseMs;
    private readonly Dictionary<MapInstance, Navigator> m_navigators = new();
    private readonly List<MonsterEntity> m_monsters = new();
    private readonly List<PendingRespawn> m_respawns = new();
    private readonly List<PendingRespawn> m_due = new();

    public MonsterAiSystem(
        WorldSimulation world,
        IRandomSource random,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation,
        ServerInstruments instruments)
    {
        m_world = world;
        m_random = random;
        m_instruments = instruments;
        m_tickRate = simulation.Value.TickRate;
        m_corpseMs = worldOptions.Value.MonsterCorpseMs;
        foreach (MapInstance map in world.Maps)
        {
            m_navigators.Add(map, new Navigator(map.Definition.Navigation));
        }
    }

    public TickPhase Phase => TickPhase.MonsterAi;

    public void Execute(in TickContext context)
    {
        long now = (long)(context.Tick - 1) * MillisecondsPerSecond / m_tickRate;
        float stepDistance = context.DeltaSeconds;
        foreach (MapInstance map in m_world.Maps)
        {
            m_monsters.Clear();
            m_monsters.AddRange(map.Monsters);
            foreach (MonsterEntity monster in m_monsters)
            {
                Update(map, monster, now, monster.MovementSpeed * stepDistance);
            }
        }

        Respawn(now);
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static PlayerEntity? LivePlayer(MapInstance map, EntityId entity)
    {
        return entity != default && map.TryGetPlayer(entity, out PlayerEntity? player) && player != null &&
            !player.IsDead
                ? player
                : null;
    }

    private static bool IsInReach(MapInstance map, MonsterEntity monster, WorldEntity target)
    {
        return HorizontalDistance(monster.Position, target.Position) <= monster.AttackRange
            && map.Definition.Navigation.HasLineOfSight(monster.Position, target.Position);
    }

    private void Update(MapInstance map, MonsterEntity monster, long now, float stepDistance)
    {
        MonsterBrain brain = monster.Brain;
        if (monster.IsDead)
        {
            OnDead(map, monster, now);
            return;
        }

        if (brain.NextDecisionMs == long.MinValue)
        {
            brain.NextDecisionMs = now;
            brain.IdleUntilMs = now + DrawPause(monster.Definition);
        }

        if (now >= brain.NextDecisionMs)
        {
            // Carried like an attack schedule: each decision adds the interval to the previous one's time.
            while (brain.NextDecisionMs <= now)
            {
                brain.NextDecisionMs += monster.Definition.ScanIntervalMs;
            }

            brain.Decisions++;
            Decide(map, monster, now);
        }

        Steer(map, monster, stepDistance);
    }

    private void Decide(MapInstance map, MonsterEntity monster, long now)
    {
        MonsterBrain brain = monster.Brain;
        if (brain.State == MonsterAiState.ReturnHome)
        {
            if (HorizontalDistance(monster.Position, monster.Home) <= HomeArrivalDistance || !brain.Path.IsActive)
            {
                BecomeIdle(monster, now);
            }

            return;
        }

        PlayerEntity? target = LivePlayer(map, monster.Target);
        if (target == null && brain.State == MonsterAiState.Chase)
        {
            ReturnHome(map, monster, now);
            return;
        }

        if (target == null)
        {
            target = Acquire(map, monster);
            if (target != null)
            {
                monster.Target = target.Id;
                monster.Combat.IsAutoAttacking = true;
                brain.State = MonsterAiState.Chase;
                brain.Path.Cancel();
                m_instruments.RecordAcquisition(monster.Definition.Id);
            }
        }

        if (target != null)
        {
            if (HorizontalDistance(monster.Position, monster.Home) > monster.Definition.LeashRadius)
            {
                ReturnHome(map, monster, now);
            }
            else if (!IsInReach(map, monster, target)
                     && (!brain.Path.IsActive ||
                         HorizontalDistance(brain.ChaseGoal, target.Position) > ChaseRepathDistance))
            {
                m_navigators[map].TryFollow(brain.Path, monster.Position, target.Position);
                brain.ChaseGoal = target.Position;
            }

            return;
        }

        if (brain.State == MonsterAiState.Idle && now >= brain.IdleUntilMs)
        {
            Navigator navigator = m_navigators[map];
            double radius = monster.Definition.RoamRadius;
            bool hasGoal = navigator.Placement.TryChoosePoint(monster.Home, radius, m_random, out WorldPosition goal);
            if (hasGoal && navigator.TryFollow(brain.Path, monster.Position, goal))
            {
                brain.State = MonsterAiState.Roam;
            }
            else
            {
                BecomeIdle(monster, now);
            }
        }
        else if (brain.State == MonsterAiState.Roam && !brain.Path.IsActive)
        {
            BecomeIdle(monster, now);
        }
    }

    private PlayerEntity? Acquire(MapInstance map, MonsterEntity monster)
    {
        PlayerEntity? attacker = LivePlayer(map, monster.Brain.LastAttacker);
        monster.Brain.LastAttacker = default;
        if (attacker != null || monster.Definition.Behavior != MonsterBehavior.Aggressive)
        {
            return attacker;
        }

        PlayerEntity? nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (PlayerEntity player in map.Players)
        {
            float distance = HorizontalDistance(monster.Position, player.Position);
            bool isCloser = distance < nearestDistance
                || (distance == nearestDistance && nearest != null && player.Id.Value < nearest.Id.Value);
            if (!player.IsDead && distance <= monster.Definition.PerceptionRadius && isCloser)
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void Steer(MapInstance map, MonsterEntity monster, float stepDistance)
    {
        MonsterBrain brain = monster.Brain;
        PlayerEntity? target = LivePlayer(map, monster.Target);
        if (monster.Combat.IsSwinging)
        {
            // A monster never walks during its own swing. The path is kept: a monster that leashed mid-swing still
            // walks home once the swing is over.
            brain.DesiredDirection = default;
            return;
        }

        if (target != null && IsInReach(map, monster, target))
        {
            brain.Path.Cancel();
            brain.DesiredDirection = default;
            return;
        }

        brain.DesiredDirection = brain.Path.Advance(monster.Position, stepDistance);
    }

    private void ReturnHome(MapInstance map, MonsterEntity monster, long now)
    {
        MonsterBrain brain = monster.Brain;
        monster.Target = default;
        monster.Combat.IsAutoAttacking = false;
        brain.LastAttacker = default;
        brain.State = MonsterAiState.ReturnHome;
        if (!m_navigators[map].TryFollow(brain.Path, monster.Position, monster.Home))
        {
            BecomeIdle(monster, now);
        }
    }

    private void BecomeIdle(MonsterEntity monster, long now)
    {
        MonsterBrain brain = monster.Brain;
        brain.State = MonsterAiState.Idle;
        brain.Path.Cancel();
        brain.DesiredDirection = default;
        brain.IdleUntilMs = now + DrawPause(monster.Definition);
    }

    private long DrawPause(MonsterDefinition definition)
    {
        return definition.IdlePauseMinMs + m_random.Next(definition.IdlePauseMaxMs - definition.IdlePauseMinMs + 1);
    }

    private void OnDead(MapInstance map, MonsterEntity monster, long now)
    {
        MonsterBrain brain = monster.Brain;
        if (brain.State != MonsterAiState.Dead)
        {
            brain.State = MonsterAiState.Dead;
            brain.DiedAtMs = now;
            brain.Path.Cancel();
            brain.DesiredDirection = default;
            long delay = Math.Max(MinimumRespawnMs, monster.Spawn.RespawnMs);
            m_respawns.Add(new PendingRespawn(map, monster.Spawn, now + delay));
        }

        if (now >= brain.DiedAtMs + m_corpseMs)
        {
            map.Remove(monster);
        }
    }

    private void Respawn(long now)
    {
        m_due.Clear();
        foreach (PendingRespawn respawn in m_respawns)
        {
            if (respawn.DueMs <= now)
            {
                m_due.Add(respawn);
            }
        }

        foreach (PendingRespawn respawn in m_due)
        {
            m_respawns.Remove(respawn);
            m_world.SpawnMonster(respawn.Map, respawn.Spawn);
        }
    }

    private sealed class PendingRespawn
    {
        public PendingRespawn(MapInstance map, MonsterSpawn spawn, long dueMs)
        {
            Map = map;
            Spawn = spawn;
            DueMs = dueMs;
        }

        public MapInstance Map { get; }

        public MonsterSpawn Spawn { get; }

        public long DueMs { get; }
    }

    private sealed class Navigator
    {
        private readonly GridPathfinder m_pathfinder;
        private readonly List<WorldPosition> m_waypoints = new();

        public Navigator(NavigationGrid grid)
        {
            m_pathfinder = new GridPathfinder(grid);
            Placement = new MonsterPlacement(grid);
        }

        public MonsterPlacement Placement { get; }

        public bool TryFollow(PathFollower path, WorldPosition from, WorldPosition goal)
        {
            if (!m_pathfinder.TryFindPath(from, goal, PathNodeBudget, m_waypoints))
            {
                path.Cancel();
                return false;
            }

            path.Follow(m_waypoints);
            return true;
        }
    }
}
}
