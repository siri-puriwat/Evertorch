using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
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
    private readonly CharacterStats m_stats;
    private readonly IMovementRules m_movementRules;
    private readonly IRandomSource m_random;
    private readonly JobDefinition m_startingJob;
    private readonly DerivedStats m_startingStats;
    private long m_lastEntityId;

    public WorldSimulation(
        ServerContent content,
        IOptions<WorldOptions> options,
        CharacterStats stats,
        IMovementRules movementRules,
        IRandomSource random)
    {
        m_content = content;
        m_stats = stats;
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
        m_startingStats = stats.Calculate(job, StartingLevel, job.StartingStats);

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

    public bool TryGetMap(MapDefinitionId id, out MapInstance? map)
    {
        return m_maps.TryGetValue(id, out map);
    }

    /// <summary>
    ///     A new character named <paramref name="name" />: <c>World:StartingJob</c> at level 1, the job's starting
    ///     statistics, full HP and SP, at its starting map's spawn point (Persistence §4).
    /// </summary>
    public NewCharacter CreateCharacter(string name, DateTime now)
    {
        MapDefinition map = m_maps[m_startingJob.StartingMap].Definition;
        return new NewCharacter(
            name,
            m_startingJob.Id,
            m_startingJob.StartingStats,
            m_startingStats.MaxHp,
            m_startingStats.MaxSp,
            map.Id,
            map.SpawnPosition,
            now);
    }

    /// <summary>
    ///     Places a stored character in the world (Persistence §6, §8): its stored job, level, experience, and
    ///     statistics, on its stored map, with its HP and SP within their maximums. It stands where it was
    ///     checkpointed, at the map's spawn point with full HP and SP when it was checkpointed dead, and at the spawn
    ///     point with its HP when its spot is no longer standable or lies inside a portal (Gameplay Systems §4.2).
    ///     Returns false, placing nothing, when the content does
    ///     not define its job, its map, or one of its items; <paramref name="problem" /> then names what is missing.
    /// </summary>
    public bool TrySpawnPlayer(
        StoredCharacter stored,
        ConnectionId owner,
        out PlayerEntity? player,
        out MapInstance? map,
        out string problem)
    {
        player = null;
        map = null;
        if (!JobDefinitionId.TryCreate(stored.JobDefinitionId, out JobDefinitionId jobId)
            || !m_content.Jobs.TryGetValue(jobId, out JobDefinition? job))
        {
            problem = $"job '{stored.JobDefinitionId}'";
            return false;
        }

        if (!MapDefinitionId.TryCreate(stored.MapDefinitionId, out MapDefinitionId mapId)
            || !m_maps.TryGetValue(mapId, out MapInstance? instance))
        {
            problem = $"map '{stored.MapDefinitionId}'";
            return false;
        }

        ItemEquipment? weapon = null;
        ItemEquipment? armor = null;
        foreach (StoredItem item in stored.Items)
        {
            if (!ItemDefinitionId.TryCreate(item.ItemDefinitionId, out ItemDefinitionId itemId)
                || !m_content.Items.TryGetValue(itemId, out ItemDefinition? itemDefinition))
            {
                problem = $"item '{item.ItemDefinitionId}'";
                return false;
            }

            // A row worn in a slot its item no longer fills is a content change the character cannot load under.
            EquipmentSlot slot = CharacterInventory.SlotOf(item);
            if (slot != EquipmentSlot.None && slot != itemDefinition!.Slot)
            {
                problem = $"{(slot == EquipmentSlot.Weapon ? "weapon" : "armor")} '{item.ItemDefinitionId}'";
                return false;
            }

            if (slot == EquipmentSlot.Weapon)
            {
                weapon = itemDefinition!.Equipment;
            }
            else if (slot == EquipmentSlot.Armor)
            {
                armor = itemDefinition!.Equipment;
            }
        }

        int level = Math.Max(StartingLevel, stored.BaseLevel);
        DerivedStats stats = m_stats.Calculate(job, level, stored.Stats);
        MapDefinition definition = instance.Definition;
        NavigationGrid grid = definition.Navigation;
        bool isStandable = grid.CanOccupy(stored.Position.X, stored.Position.Z)
            && grid.TrySampleHeight(stored.Position.X, stored.Position.Z, out float _)
            && !definition.IsInPortal(stored.Position);
        bool wasDead = stored.Health <= 0;
        WorldPosition position = wasDead || !isStandable ? definition.SpawnPosition : stored.Position;
        float speed = m_movementRules.CalculateMovement(new MovementContext(stats.MovementSpeed)).Speed;
        player = new PlayerEntity(
            NextEntityId(),
            new CharacterId(stored.Id),
            owner,
            job.Id,
            position,
            MovementModel.NormalizeOrZero(definition.SpawnFacing.X, definition.SpawnFacing.Z),
            speed,
            level,
            stored.Stats,
            stats,
            m_stats.CalculateRegeneration(stored.Stats, stats),
            (float)m_content.Skills[job.BasicAttack].Range);

        // Equipment loads with the character and counts from its first tick (Gameplay Systems §11.1), so the stored
        // HP and SP are held to the maximums it gives.
        player.Weapon = weapon;
        player.Armor = armor;
        m_stats.Recalculate(player, job);
        player.CurrentHealth = wasDead ? player.MaxHealth : Math.Min(stored.Health, player.MaxHealth);
        player.CurrentSpirit = wasDead ? player.MaxSpirit : Math.Min(stored.Spirit, player.MaxSpirit);
        player.Experience = stored.Experience;
        instance.Add(player);
        map = instance;
        problem = string.Empty;
        return true;
    }

    public void RemovePlayer(MapInstance map, PlayerEntity player)
    {
        map.Remove(player);
    }

    /// <summary>
    ///     Places a new monster of <paramref name="spawn" /> at a fresh random point of its area.
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

    public ItemDropEntity SpawnItemDrop(
        MapInstance map,
        ItemDefinitionId item,
        uint amount,
        WorldPosition position,
        uint tick,
        long expiresAtMs,
        CharacterId priority)
    {
        // Not from the seeded random: a drop's identity must be unique across restarts, and drawing it from the
        // gameplay random would change every roll after it.
        var drop = new ItemDropEntity(NextEntityId(), Guid.NewGuid(), item, amount, position, tick, expiresAtMs,
            priority);
        map.Add(drop);
        return drop;
    }

    private EntityId NextEntityId()
    {
        m_lastEntityId++;
        return new EntityId(m_lastEntityId);
    }
}
}
