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

    /// <summary>A skill's try draws below this; its chance is the share of it that casts.</summary>
    public const int TryScale = 1_000_000;

    private const int MillisecondsPerSecond = 1000;
    private const int PathNodeBudget = 8192;

    // Straight away from the target first, then turned counterclockwise and clockwise, seen from above.
    private static readonly double[] RetreatTurnsDegrees = { 0d, 45d, -45d, 90d, -90d };

    private readonly WorldSimulation m_world;
    private readonly IRandomSource m_random;
    private readonly ServerInstruments m_instruments;
    private readonly CombatSystem m_combat;
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
        ServerInstruments instruments,
        CombatSystem combat)
    {
        m_world = world;
        m_random = random;
        m_instruments = instruments;
        m_combat = combat;
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
                Update(map, monster, now, context.Tick, monster.MovementSpeed * stepDistance);
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

    /// <summary>
    ///     Whether a skill with <paramref name="chance" /> is cast on a try that drew <paramref name="draw" /> below
    ///     <see cref="TryScale" /> (skills research note).
    /// </summary>
    public static bool IsTried(double chance, int draw)
    {
        return draw < chance * TryScale;
    }

    private void Update(MapInstance map, MonsterEntity monster, long now, uint tick, float stepDistance)
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
            Decide(map, monster, now, tick);
        }

        // The combat phase, which runs before this one, began the swing this tick.
        if (monster.Definition.Assists && monster.Combat.IsSwinging && monster.Combat.SwingStartTick == tick)
        {
            CallKin(map, monster, now);
        }

        Steer(map, monster, stepDistance);
    }

    /// <summary>
    ///     An assisting monster's call to its kind (Gameplay Systems §10; monster AI research note): at most once a
    ///     second, every other live monster of its definition with no target, not walking home, that has not answered
    ///     in the last second, within the assist radius, and in sight of the caller takes the caller's target and
    ///     chases it. Returns how many answered.
    /// </summary>
    public int CallKin(MapInstance map, MonsterEntity caller, long now)
    {
        PlayerEntity? target = LivePlayer(map, caller.Target);
        if (target == null || now < caller.Brain.NextCallMs)
        {
            return 0;
        }

        caller.Brain.NextCallMs = now + MillisecondsPerSecond;
        int answered = 0;
        foreach (MonsterEntity kin in map.Monsters)
        {
            MonsterBrain brain = kin.Brain;
            if (kin == caller
                || kin.IsDead
                || kin.Definition.Id != caller.Definition.Id
                || kin.Target != default
                || brain.State == MonsterAiState.ReturnHome
                || now < brain.NextAnswerMs
                || HorizontalDistance(caller.Position, kin.Position) > caller.Definition.AssistRadius
                || !map.Definition.Navigation.HasLineOfSight(caller.Position, kin.Position))
            {
                continue;
            }

            brain.NextAnswerMs = now + MillisecondsPerSecond;
            kin.Target = target.Id;
            kin.Combat.IsAutoAttacking = true;
            brain.State = MonsterAiState.Chase;
            brain.Path.Cancel();
            m_instruments.RecordAssist(kin.Definition.Id);
            answered++;
        }

        return answered;
    }

    private void Decide(MapInstance map, MonsterEntity monster, long now, uint tick)
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
            else if (TryRetreat(map, monster, target) || TryCast(map, monster, target, tick))
            {
                // A retreat replaces the decision's skill tries, and a cast holds the monster where it stands.
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

    // A monster keeping its range walks away from a target that came nearer than its keep distance (Gameplay Systems
    // §10): to the first candidate at (keepDistance + attackRange) ÷ 2 from the target that is standable, reachable,
    // and within its leash, straight away or turned by ±45° or ±90°. With none it stays and attacks; a swing or a cast
    // holds it anyway. The walk goes to that one goal, and the monster decides again once it stands.
    private bool TryRetreat(MapInstance map, MonsterEntity monster, PlayerEntity target)
    {
        if (monster.Brain.IsRetreating)
        {
            return true;
        }

        MonsterDefinition definition = monster.Definition;
        float distance = HorizontalDistance(monster.Position, target.Position);
        if (definition.KeepDistance <= 0d
            || distance >= definition.KeepDistance
            || monster.Combat.IsSwinging
            || monster.Combat.IsCasting)
        {
            return false;
        }

        // A target standing on the monster gives no direction; it backs away from its own facing.
        double awayX = distance > 0f ? (monster.Position.X - target.Position.X) / distance : -monster.Facing.X;
        double awayZ = distance > 0f ? (monster.Position.Z - target.Position.Z) / distance : -monster.Facing.Z;
        double reach = (definition.KeepDistance + definition.AttackRange) / 2d;
        NavigationGrid grid = map.Definition.Navigation;
        foreach (double degrees in RetreatTurnsDegrees)
        {
            double radians = degrees * Math.PI / 180d;
            float goalX = (float)(target.Position.X + reach * (awayX * Math.Cos(radians) - awayZ * Math.Sin(radians)));
            float goalZ = (float)(target.Position.Z + reach * (awayX * Math.Sin(radians) + awayZ * Math.Cos(radians)));
            if (!grid.CanOccupy(goalX, goalZ) || !grid.TrySampleHeight(goalX, goalZ, out float height))
            {
                continue;
            }

            var goal = new WorldPosition(goalX, height, goalZ);
            if (HorizontalDistance(goal, monster.Home) <= definition.LeashRadius
                && m_navigators[map].TryFollow(monster.Brain.Path, monster.Position, goal))
            {
                // Its swings wait until it stands again, or the first would hold it beside the target.
                monster.Brain.IsRetreating = true;
                monster.Combat.IsAutoAttacking = false;
                m_instruments.RecordRetreat(definition.Id);
                return true;
            }
        }

        return false;
    }

    // Each skill the monster could cast now draws once from the AI's source, in content order; the first success
    // casts (skills research note).
    private bool TryCast(MapInstance map, MonsterEntity monster, PlayerEntity target, uint tick)
    {
        foreach (MonsterSkill entry in monster.Definition.Skills)
        {
            if (m_combat.CanMonsterCast(map, monster, entry.Skill, target, tick)
                && IsTried(entry.Chance, m_random.Next(TryScale)))
            {
                m_combat.BeginMonsterCast(map, monster, entry.Skill, target, tick);
                return true;
            }
        }

        return false;
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

        if (brain.IsRetreating && !brain.Path.IsActive)
        {
            EndRetreat(monster);
        }

        // A retreat walks on although the target is in reach: getting out of its keep distance is the point.
        if (target != null && IsInReach(map, monster, target) && !brain.IsRetreating)
        {
            brain.Path.Cancel();
            brain.DesiredDirection = default;
            return;
        }

        brain.DesiredDirection = brain.Path.Advance(monster.Position, stepDistance);
    }

    private static void EndRetreat(MonsterEntity monster)
    {
        if (monster.Brain.IsRetreating)
        {
            monster.Brain.IsRetreating = false;
            monster.Combat.IsAutoAttacking = monster.Target != default;
        }
    }

    private void ReturnHome(MapInstance map, MonsterEntity monster, long now)
    {
        MonsterBrain brain = monster.Brain;
        monster.Target = default;
        monster.Combat.IsAutoAttacking = false;
        brain.LastAttacker = default;
        brain.IsRetreating = false;
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
        brain.IsRetreating = false;
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
