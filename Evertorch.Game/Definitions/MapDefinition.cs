using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
public sealed class MapDefinition
{
    public MapDefinition(
        MapDefinitionId id,
        string displayName,
        WorldPosition spawnPosition,
        WorldDirection spawnFacing,
        IReadOnlyList<MonsterSpawn> monsterSpawns,
        NavigationGrid navigation,
        IReadOnlyList<MapPortal>? portals = null,
        IReadOnlyList<NpcPlacement>? npcs = null)
    {
        Id = id;
        DisplayName = displayName;
        SpawnPosition = spawnPosition;
        SpawnFacing = spawnFacing;
        MonsterSpawns = monsterSpawns;
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Portals = portals ?? Array.Empty<MapPortal>();
        Npcs = npcs ?? Array.Empty<NpcPlacement>();
    }

    public MapDefinitionId Id { get; }

    public string DisplayName { get; }

    public WorldPosition SpawnPosition { get; }

    public WorldDirection SpawnFacing { get; }

    public IReadOnlyList<MonsterSpawn> MonsterSpawns { get; }

    public NavigationGrid Navigation { get; }

    /// <summary>
    ///     The ways to other maps; none on a map without portals.
    /// </summary>
    public IReadOnlyList<MapPortal> Portals { get; }

    /// <summary>
    ///     The NPCs the map places; none on a map without NPCs.
    /// </summary>
    public IReadOnlyList<NpcPlacement> Npcs { get; }

    /// <summary>
    ///     Whether <paramref name="position" /> is inside one of the map's portals.
    /// </summary>
    public bool IsInPortal(WorldPosition position)
    {
        foreach (MapPortal portal in Portals)
        {
            if (portal.Contains(position))
            {
                return true;
            }
        }

        return false;
    }
}
}
