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

        NavigationGrid? navigation = NavigationReader.Read(root.RequiredMapping("navigation"), diagnostics);

        YamlFieldReader client = root.RequiredMapping("client");
        string scene = client.RequiredAssetKey("scene");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore || navigation == null)
        {
            return null;
        }

        CheckPlacement(navigation, spawnPoint, spawnPosition, spawnReaders, monsterSpawns);
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
            navigation);
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

        return new MonsterSpawn(monster, center, radius, count, respawnMs);
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
