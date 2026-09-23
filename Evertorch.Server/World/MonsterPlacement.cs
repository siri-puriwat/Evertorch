using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     Draws points uniformly within a radius that a body can stand on and walk to from the center (Gameplay Systems
///     §10). Every draw that falls outside the circle, on blocked ground, or out of reach counts against the limit.
/// </summary>
public sealed class MonsterPlacement
{
    public const int MaxDraws = 16;

    private const int PathNodeBudget = 8192;
    private const int Resolution = 1_000_000;

    private readonly NavigationGrid m_grid;
    private readonly GridPathfinder m_pathfinder;
    private readonly List<WorldPosition> m_waypoints = new();

    public MonsterPlacement(NavigationGrid grid)
    {
        m_grid = grid;
        m_pathfinder = new GridPathfinder(grid);
    }

    /// <summary>
    ///     Where a monster of <paramref name="spawn" /> appears; the spawn center after <see cref="MaxDraws" />
    ///     unsuitable draws, which content validation guarantees is standable.
    /// </summary>
    public WorldPosition ChooseSpawnPoint(MonsterSpawn spawn, IRandomSource random)
    {
        return TryChoosePoint(spawn.Center, spawn.Radius, random, out WorldPosition point) ? point : spawn.Center;
    }

    public bool TryChoosePoint(WorldPosition center, double radius, IRandomSource random, out WorldPosition point)
    {
        for (int draw = 0; draw < MaxDraws; draw++)
        {
            double offsetX = radius * (2.0 * random.Next(Resolution) / Resolution - 1.0);
            double offsetZ = radius * (2.0 * random.Next(Resolution) / Resolution - 1.0);
            if (offsetX * offsetX + offsetZ * offsetZ > radius * radius)
            {
                continue;
            }

            float x = (float)(center.X + offsetX);
            float z = (float)(center.Z + offsetZ);
            if (!m_grid.CanOccupy(x, z) || !m_grid.TrySampleHeight(x, z, out float height))
            {
                continue;
            }

            var candidate = new WorldPosition(x, height, z);
            if (m_pathfinder.TryFindPath(center, candidate, PathNodeBudget, m_waypoints))
            {
                point = candidate;
                return true;
            }
        }

        point = center;
        return false;
    }
}
}
