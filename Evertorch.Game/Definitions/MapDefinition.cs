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
        IReadOnlyList<MonsterSpawn> monsterSpawns)
    {
        Id = id;
        DisplayName = displayName;
        SpawnPosition = spawnPosition;
        SpawnFacing = spawnFacing;
        MonsterSpawns = monsterSpawns;
    }

    public MapDefinitionId Id { get; }

    public string DisplayName { get; }

    public WorldPosition SpawnPosition { get; }

    public WorldDirection SpawnFacing { get; }

    public IReadOnlyList<MonsterSpawn> MonsterSpawns { get; }
}
}
