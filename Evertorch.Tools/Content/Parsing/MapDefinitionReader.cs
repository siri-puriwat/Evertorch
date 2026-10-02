using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class MapDefinitionReader
{
    private const float GroundHeightTolerance = 0.01f;

    public static AuthoredMap? Read(
        YamlFieldReader root,
        List<ContentDiagnostic> diagnostics,
        ISet<string> declaredIds)
    {
        int errorsBefore = diagnostics.Count;

        MapDefinitionId id = root.RequiredId<MapDefinitionId>(
            "id",
            MapDefinitionId.TryCreate,
            MapDefinitionId.KindPrefix);
        if (id != default)
        {
            declaredIds.Add(id.Value);
        }

        string displayName = root.RequiredString("displayName");

        YamlFieldReader server = root.RequiredMapping("server");

        YamlFieldReader spawnPoint = server.RequiredMapping("spawnPoint");
        WorldPosition spawnPosition = ReadPosition(spawnPoint.RequiredMapping("position"));
        YamlFieldReader facing = spawnPoint.RequiredMapping("facing");
        var spawnFacing = new WorldDirection(ReadCoordinate(facing, "x"), ReadCoordinate(facing, "z"));
        if (diagnostics.Count == errorsBefore && spawnFacing == new WorldDirection(0f, 0f))
        {
            spawnPoint.ReportField("facing", "must not be the zero direction");
        }

        var monsterSpawns = new List<MonsterSpawn>();
        IReadOnlyList<YamlFieldReader> spawnReaders = server.OptionalMappingSequence("monsterSpawns");
        foreach (YamlFieldReader spawn in spawnReaders)
        {
            monsterSpawns.Add(ReadSpawn(spawn));
        }

        var portals = new List<MapPortal>();
        IReadOnlyList<YamlFieldReader> portalReaders = server.OptionalMappingSequence("portals");
        foreach (YamlFieldReader portal in portalReaders)
        {
            MapPortal? read = ReadPortal(portal);
            if (read != null)
            {
                portals.Add(read);
            }
        }

        var npcs = new List<NpcPlacement>();
        IReadOnlyList<YamlFieldReader> npcReaders = server.OptionalMappingSequence("npcs");
        foreach (YamlFieldReader npc in npcReaders)
        {
            NpcPlacement? read = ReadNpc(npc);
            if (read != null)
            {
                npcs.Add(read);
            }
        }

        NavigationGrid? navigation = NavigationReader.Read(root.RequiredMapping("navigation"), diagnostics);

        YamlFieldReader client = root.RequiredMapping("client");
        string scene = client.RequiredAssetKey("scene");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore || navigation == null)
        {
            return null;
        }

        CheckPlacement(navigation, spawnPoint, spawnPosition, spawnReaders, monsterSpawns);
        CheckPortals(navigation, spawnPoint, spawnPosition, portalReaders, portals);
        CheckNpcs(navigation, spawnPosition, npcReaders, npcs);
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        var definition = new MapDefinition(
            id,
            displayName,
            spawnPosition,
            spawnFacing,
            monsterSpawns,
            navigation,
            portals,
            npcs);
        return new AuthoredMap(root.ToSource(), definition, scene);
    }

    // A spawn point inside a wall, or a monster area nobody can walk to, is a broken map rather than a tuning choice.
    private static void CheckPlacement(
        NavigationGrid navigation,
        YamlFieldReader spawnPoint,
        WorldPosition spawnPosition,
        IReadOnlyList<YamlFieldReader> spawnReaders,
        List<MonsterSpawn> monsterSpawns)
    {
        if (!CheckStandable(navigation, spawnPoint, "position", spawnPosition))
        {
            return;
        }

        var pathfinder = new GridPathfinder(navigation);
        var waypoints = new List<WorldPosition>();
        int nodeBudget = navigation.Columns * navigation.Rows;
        for (int index = 0; index < monsterSpawns.Count; index++)
        {
            WorldPosition center = monsterSpawns[index].Center;
            if (CheckStandable(navigation, spawnReaders[index], "center", center)
                && !pathfinder.TryFindPath(spawnPosition, center, nodeBudget, waypoints))
            {
                spawnReaders[index].ReportField("center", "cannot be reached from the spawn point");
            }
        }
    }

    // A portal nobody can walk into is useless, and a spawn point inside one would send every arrival on at once.
    private static void CheckPortals(
        NavigationGrid navigation,
        YamlFieldReader spawnPoint,
        WorldPosition spawnPosition,
        IReadOnlyList<YamlFieldReader> portalReaders,
        List<MapPortal> portals)
    {
        // An unstandable spawn point is already reported, and nothing is reachable from it.
        if (!navigation.CanOccupy(spawnPosition.X, spawnPosition.Z))
        {
            return;
        }

        var pathfinder = new GridPathfinder(navigation);
        var waypoints = new List<WorldPosition>();
        int nodeBudget = navigation.Columns * navigation.Rows;
        for (int index = 0; index < portals.Count; index++)
        {
            MapPortal portal = portals[index];
            if (CheckStandable(navigation, portalReaders[index], "center", portal.Center)
                && !pathfinder.TryFindPath(spawnPosition, portal.Center, nodeBudget, waypoints))
            {
                portalReaders[index].ReportField("center", "cannot be reached from the spawn point");
            }

            if (portal.Contains(spawnPosition))
            {
                spawnPoint.ReportField("position", "lies inside a portal");
            }
        }
    }

    // An NPC stands on a marker cell of its own, which no body can enter, so a player needs a place to stand within
    // reach of it that can be walked to (Gameplay Systems §6.1).
    private static void CheckNpcs(
        NavigationGrid navigation,
        WorldPosition spawnPosition,
        IReadOnlyList<YamlFieldReader> npcReaders,
        List<NpcPlacement> npcs)
    {
        // An unstandable spawn point is already reported, and nothing is reachable from it.
        if (!navigation.CanOccupy(spawnPosition.X, spawnPosition.Z))
        {
            return;
        }

        var pathfinder = new GridPathfinder(navigation);
        var waypoints = new List<WorldPosition>();
        var places = new List<WorldPosition>();
        var markers = new HashSet<(int Column, int Row)>();
        int nodeBudget = navigation.Columns * navigation.Rows;
        for (int index = 0; index < npcs.Count; index++)
        {
            WorldPosition position = npcs[index].Position;
            YamlFieldReader reader = npcReaders[index];
            if (!navigation.TryGetCellIndex(position.X, position.Z, out int column, out int row)
                || navigation.GetCell(column, row).Surface != NavigationSurface.NpcMarker)
            {
                reader.ReportField("position", "is not on an NPC marker cell");
                continue;
            }

            float height = navigation.GetCell(column, row).HeightAtMin;
            if (Math.Abs(position.Y - height) > GroundHeightTolerance)
            {
                reader.ReportField(
                    "position",
                    string.Format(CultureInfo.InvariantCulture, "y must be the marker's height {0}", height));
            }

            if (!markers.Add((column, row)))
            {
                reader.ReportField("position", "is on a marker cell where another NPC already stands");
            }

            navigation.CollectStandingPlaces(position, NpcInteraction.Range, places);
            bool isReachable = false;
            foreach (WorldPosition place in places)
            {
                if (pathfinder.TryFindPath(spawnPosition, place, nodeBudget, waypoints))
                {
                    isReachable = true;
                    break;
                }
            }

            if (!isReachable)
            {
                reader.ReportField(
                    "position",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "has no place to stand within {0} m that can be reached from the spawn point",
                        NpcInteraction.Range));
            }
        }
    }

    private static NpcPlacement? ReadNpc(YamlFieldReader npc)
    {
        NpcDefinitionId id =
            npc.RequiredId<NpcDefinitionId>("npc", NpcDefinitionId.TryCreate, NpcDefinitionId.KindPrefix);
        WorldPosition position = ReadPosition(npc.RequiredMapping("position"));
        YamlFieldReader facingReader = npc.RequiredMapping("facing");
        var facing = new WorldDirection(ReadCoordinate(facingReader, "x"), ReadCoordinate(facingReader, "z"));
        if (facing == new WorldDirection(0f, 0f))
        {
            npc.ReportField("facing", "must not be the zero direction");
            return null;
        }

        return id != default ? new NpcPlacement(id, position, facing) : null;
    }

    private static MapPortal? ReadPortal(YamlFieldReader portal)
    {
        WorldPosition center = ReadPosition(portal.RequiredMapping("center"));
        double radius = portal.RequiredDouble("radius", 0d, ContentLimits.MaxDistance, true);
        YamlFieldReader destination = portal.RequiredMapping("destination");
        MapDefinitionId map = destination.RequiredId<MapDefinitionId>(
            "map",
            MapDefinitionId.TryCreate,
            MapDefinitionId.KindPrefix);
        WorldPosition position = ReadPosition(destination.RequiredMapping("position"));
        YamlFieldReader facingReader = destination.RequiredMapping("facing");
        var facing = new WorldDirection(ReadCoordinate(facingReader, "x"), ReadCoordinate(facingReader, "z"));
        if (facing == new WorldDirection(0f, 0f))
        {
            destination.ReportField("facing", "must not be the zero direction");
            return null;
        }

        return radius > 0d && map != default ? new MapPortal(center, radius, map, position, facing) : null;
    }

    private static bool CheckStandable(
        NavigationGrid navigation,
        YamlFieldReader reader,
        string field,
        WorldPosition position)
    {
        if (!navigation.CanOccupy(position.X, position.Z)
            || !navigation.TrySampleHeight(position.X, position.Z, out float groundHeight))
        {
            reader.ReportField(field, "is not a place the navigation grid lets an agent stand");
            return false;
        }

        if (Math.Abs(position.Y - groundHeight) > GroundHeightTolerance)
        {
            reader.ReportField(
                field,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "y must be the ground height {0} at that point",
                    groundHeight));
            return false;
        }

        return true;
    }

    private static MonsterSpawn ReadSpawn(YamlFieldReader spawn)
    {
        MonsterDefinitionId monster = spawn.RequiredId<MonsterDefinitionId>(
            "monster",
            MonsterDefinitionId.TryCreate,
            MonsterDefinitionId.KindPrefix);
        WorldPosition center = ReadPosition(spawn.RequiredMapping("center"));
        double radius = spawn.RequiredDouble("radius", 0d, ContentLimits.MaxDistance, false);
        int count = spawn.RequiredInt("count", 1, ContentLimits.MaxSpawnCount);
        int respawnMs = spawn.RequiredInt("respawnMs", 0, ContentLimits.MaxDurationMs);
        int respawnVarianceMs = spawn.Has("respawnVarianceMs")
            ? spawn.RequiredInt("respawnVarianceMs", 0, ContentLimits.MaxDurationMs)
            : 0;
        if (respawnVarianceMs > MonsterSpawn.MaxRespawnVarianceMs(respawnMs))
        {
            spawn.ReportField("respawnVarianceMs", "must be at most respawnMs less 1000");
        }

        return new MonsterSpawn(monster, center, radius, count, respawnMs, respawnVarianceMs);
    }

    private static WorldPosition ReadPosition(YamlFieldReader position)
    {
        return new WorldPosition(
            ReadCoordinate(position, "x"),
            ReadCoordinate(position, "y"),
            ReadCoordinate(position, "z"));
    }

    private static float ReadCoordinate(YamlFieldReader reader, string key)
    {
        return (float)reader.RequiredDouble(key, -ContentLimits.MaxCoordinate, ContentLimits.MaxCoordinate, false);
    }
}
}
