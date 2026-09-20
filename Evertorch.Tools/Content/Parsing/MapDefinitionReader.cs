using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
internal static class MapDefinitionReader
{
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
        WorldDirection spawnFacing = new WorldDirection(ReadCoordinate(facing, "x"), ReadCoordinate(facing, "z"));
        if (diagnostics.Count == errorsBefore && spawnFacing == new WorldDirection(0f, 0f))
        {
            spawnPoint.ReportField("facing", "must not be the zero direction");
        }

        List<MonsterSpawn> monsterSpawns = new List<MonsterSpawn>();
        foreach (YamlFieldReader spawn in server.OptionalMappingSequence("monsterSpawns"))
        {
            monsterSpawns.Add(ReadSpawn(spawn));
        }

        YamlFieldReader client = root.RequiredMapping("client");
        string scene = client.RequiredAssetKey("scene");

        root.ReportUnknownFields();
        if (diagnostics.Count != errorsBefore)
        {
            return null;
        }

        MapDefinition definition = new MapDefinition(id, displayName, spawnPosition, spawnFacing, monsterSpawns);
        return new AuthoredMap(root.ToSource(), definition, scene);
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
