using System;

namespace Evertorch.Game
{
public sealed class MonsterSpawn
{
    public MonsterSpawn(
        MonsterDefinitionId monster,
        WorldPosition center,
        double radius,
        int count,
        int respawnMs,
        int respawnVarianceMs = 0)
    {
        Monster = monster;
        Center = center;
        Radius = radius;
        Count = count;
        RespawnMs = respawnMs;
        RespawnVarianceMs = respawnVarianceMs;
    }

    public MonsterDefinitionId Monster { get; }

    public WorldPosition Center { get; }

    public double Radius { get; }

    public int Count { get; }

    public int RespawnMs { get; }

    /// <summary>
    ///     How much sooner or later than <see cref="RespawnMs" /> a death's respawn may come (Gameplay Systems §10); 0
    ///     for none.
    /// </summary>
    public int RespawnVarianceMs { get; }

    /// <summary>
    ///     The widest spread a spawn that respawns after <paramref name="respawnMs" /> may have, which keeps every
    ///     respawn at least <see cref="ContentLimits.MinRespawnMs" /> after the death (Content Pipeline §4).
    /// </summary>
    public static int MaxRespawnVarianceMs(int respawnMs)
    {
        return Math.Max(0, respawnMs - ContentLimits.MinRespawnMs);
    }
}
}
