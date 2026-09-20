using Evertorch.Game;

namespace Evertorch.Tools
{
public sealed class MonsterSpawn
{
    public MonsterSpawn(MonsterDefinitionId monster, WorldPosition center, double radius, int count, int respawnMs)
    {
        Monster = monster;
        Center = center;
        Radius = radius;
        Count = count;
        RespawnMs = respawnMs;
    }

    public MonsterDefinitionId Monster { get; }

    public WorldPosition Center { get; }

    public double Radius { get; }

    public int Count { get; }

    public int RespawnMs { get; }
}
}
