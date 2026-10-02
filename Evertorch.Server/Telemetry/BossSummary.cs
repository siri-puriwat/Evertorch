using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One boss as an operator sees it (System Architecture §10): alive, or how long until it returns.
/// </summary>
public sealed class BossSummary
{
    public BossSummary(MonsterDefinitionId monster, MapDefinitionId map, bool isAlive, long returnsInMs)
    {
        Monster = monster;
        Map = map;
        IsAlive = isAlive;
        ReturnsInMs = returnsInMs;
    }

    public MonsterDefinitionId Monster { get; }

    public MapDefinitionId Map { get; }

    public bool IsAlive { get; }

    /// <summary>
    ///     How long until a dead boss returns; 0 for a live one.
    /// </summary>
    public long ReturnsInMs { get; }
}
}
