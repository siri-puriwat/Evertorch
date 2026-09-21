using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Rules;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
/// Owns every map this process simulates. It is built from validated content before anything connects and is
/// touched only by the tick thread afterwards.
/// </summary>
public sealed class WorldSimulation
{
    private const int StartingLevel = 1;
    private const uint FirstInstanceNumber = 1;

    private readonly Dictionary<MapDefinitionId, MapInstance> m_maps = new Dictionary<MapDefinitionId, MapInstance>();
    private readonly JobDefinition m_startingJob;
    private readonly float m_startingMovementSpeed;
    private long m_lastEntityId;

    public WorldSimulation(
        ServerContent content,
        IOptions<WorldOptions> options,
        ICharacterRules characterRules,
        IMovementRules movementRules)
    {
        WorldOptions world = options.Value;
        if (!JobDefinitionId.TryCreate(world.StartingJob, out JobDefinitionId startingJob)
            || !content.Jobs.TryGetValue(startingJob, out JobDefinition? job))
        {
            throw new InvalidOperationException(
                WorldOptions.SectionName + ":StartingJob '" + world.StartingJob + "' is not in the loaded content.");
        }

        m_startingJob = job;
        m_startingMovementSpeed = CalculateMovementSpeed(job, characterRules, movementRules);

        foreach (MapDefinition map in content.Maps.Values.OrderBy(map => map.Id.Value, StringComparer.Ordinal))
        {
            InterestGrid interest = new InterestGrid(world.InterestCellSize, world.InterestNeighborRadius);
            m_maps.Add(map.Id, new MapInstance(map, FirstInstanceNumber, interest));
        }
    }

    public IReadOnlyCollection<MapInstance> Maps => m_maps.Values;

    /// <summary>
    /// Places a new player at the spawn point of the starting job's map.
    /// </summary>
    public PlayerEntity SpawnPlayer(CharacterId character, ConnectionId owner, out MapInstance map)
    {
        map = m_maps[m_startingJob.StartingMap];
        m_lastEntityId++;
        PlayerEntity player = new PlayerEntity(
            new EntityId(m_lastEntityId),
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

    private static float CalculateMovementSpeed(
        JobDefinition job,
        ICharacterRules characterRules,
        IMovementRules movementRules)
    {
        CharacterBuild build = new CharacterBuild(
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
