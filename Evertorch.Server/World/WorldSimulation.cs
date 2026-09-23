using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Owns every map this process simulates. It is built from validated content before anything connects and is
///     touched only by the tick thread afterwards.
/// </summary>
public sealed class WorldSimulation
{
    private const int StartingLevel = 1;
    private const uint FirstInstanceNumber = 1;

    private static readonly WorldDirection MonsterFacing = new(0f, 1f);

    private readonly Dictionary<MapDefinitionId, MapInstance> m_maps = new();
    private readonly Dictionary<MapInstance, MonsterPlacement> m_placements = new();
    private readonly ServerContent m_content;
    private readonly IMovementRules m_movementRules;
    private readonly IRandomSource m_random;
    private readonly JobDefinition m_startingJob;
    private readonly float m_startingMovementSpeed;
    private long m_lastEntityId;

    public WorldSimulation(
        ServerContent content,
        IOptions<WorldOptions> options,
        ICharacterRules characterRules,
        IMovementRules movementRules,
        IRandomSource random)
    {
        m_content = content;
        m_movementRules = movementRules;
        m_random = random;
        WorldOptions world = options.Value;
        if (!JobDefinitionId.TryCreate(world.StartingJob, out JobDefinitionId startingJob)
            || !content.Jobs.TryGetValue(startingJob, out JobDefinition? job))
        {
            throw new InvalidOperationException(
                $"{WorldOptions.SectionName}:StartingJob '{world.StartingJob}' is not in the loaded content.");
        }

        m_startingJob = job;
        m_startingMovementSpeed = CalculateMovementSpeed(job, characterRules, movementRules);

        foreach (MapDefinition map in content.Maps.Values.OrderBy(map => map.Id.Value, StringComparer.Ordinal))
        {
            var interest = new InterestGrid(world.InterestCellSize, world.InterestNeighborRadius);
            var instance = new MapInstance(map, FirstInstanceNumber, interest);
            m_maps.Add(map.Id, instance);
            m_placements.Add(instance, new MonsterPlacement(map.Navigation));
            foreach (MonsterSpawn spawn in map.MonsterSpawns)
            {
                for (int index = 0; index < spawn.Count; index++)
                {
                    SpawnMonster(instance, spawn);
                }
            }
        }
    }

    public IReadOnlyCollection<MapInstance> Maps => m_maps.Values;

    /// <summary>
    ///     Places a new player at the spawn point of the starting job's map.
    /// </summary>
    public PlayerEntity SpawnPlayer(CharacterId character, ConnectionId owner, out MapInstance map)
    {
        map = m_maps[m_startingJob.StartingMap];
        var player = new PlayerEntity(
            NextEntityId(),
            character,
            owner,
            m_startingJob.Id,
            map.Definition.SpawnPosition,
            MovementModel.NormalizeOrZero(map.Definition.SpawnFacing.X, map.Definition.SpawnFacing.Z),
            m_startingMovementSpeed);
        map.Add(player);
        return player;
    }

    public void RemovePlayer(MapInstance map, PlayerEntity player)
    {
        map.Remove(player);
    }

    /// <summary>
    ///     Places a new monster of <paramref name="spawn" /> at a fresh random point of its area (Gameplay Systems §10).
    /// </summary>
    public MonsterEntity SpawnMonster(MapInstance map, MonsterSpawn spawn)
    {
        MonsterDefinition definition = m_content.Monsters[spawn.Monster];
        float speed = m_movementRules.CalculateMovement(new MovementContext((float)definition.BaseSpeed)).Speed;
        var monster = new MonsterEntity(
            NextEntityId(),
            definition,
            spawn,
            m_placements[map].ChooseSpawnPoint(spawn, m_random),
            MonsterFacing,
            speed);
        map.Add(monster);
        return monster;
    }

    private EntityId NextEntityId()
    {
        m_lastEntityId++;
        return new EntityId(m_lastEntityId);
    }

    private static float CalculateMovementSpeed(
        JobDefinition job,
        ICharacterRules characterRules,
        IMovementRules movementRules)
    {
        var build = new CharacterBuild(
            StartingLevel,
            job.StartingStats,
            job.HealthBase,
            job.HealthPerLevel,
            job.SpiritBase,
            job.SpiritPerLevel,
            job.UnarmedAttackSpeedPenalty,
            (float)job.BaseSpeed);
        DerivedStats stats = characterRules.CalculateDerivedStats(build);
        return movementRules.CalculateMovement(new MovementContext(stats.MovementSpeed)).Speed;
    }
}
}
