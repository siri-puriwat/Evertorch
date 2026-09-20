using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class MapDefinition
{
    public MapDefinition(
        DefinitionSource source,
        MapDefinitionId id,
        string displayName,
        WorldPosition spawnPosition,
        WorldDirection spawnFacing,
        IReadOnlyList<MonsterSpawn> monsterSpawns,
        string scene)
    {
        Source = source;
        Id = id;
        DisplayName = displayName;
        SpawnPosition = spawnPosition;
        SpawnFacing = spawnFacing;
        MonsterSpawns = monsterSpawns;
        Scene = scene;
    }

    public DefinitionSource Source { get; }

    public MapDefinitionId Id { get; }

    public string DisplayName { get; }

    public WorldPosition SpawnPosition { get; }

    public WorldDirection SpawnFacing { get; }

    public IReadOnlyList<MonsterSpawn> MonsterSpawns { get; }

    public string Scene { get; }
}
}
