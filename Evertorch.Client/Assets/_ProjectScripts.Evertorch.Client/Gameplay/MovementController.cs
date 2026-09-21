using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
/// Chooses this tick's direction from the two ways a player can ask to move: a held direction, or a point to walk
/// to. A held direction always wins and ends the walk in the same tick it is seen.
/// </summary>
public sealed class MovementController
{
    public const int PathNodeBudget = 8192;

    private readonly GridPathfinder m_pathfinder;
    private readonly PathFollower m_follower = new PathFollower();
    private readonly List<WorldPosition> m_foundWaypoints = new List<WorldPosition>();
    private WorldDirection m_manualDirection;

    public MovementController(NavigationGrid grid)
    {
        m_pathfinder = new GridPathfinder(grid ?? throw new ArgumentNullException(nameof(grid)));
    }

    public bool HasPath => m_follower.IsActive;

    public IReadOnlyList<WorldPosition> Path => m_follower.Waypoints;

    public int NextWaypointIndex => m_follower.NextWaypointIndex;

    public int RejectedMoveRequests { get; private set; }

    public int CancelledPaths { get; private set; }

    /// <summary>
    /// The latest held direction in world space. Any length is accepted; only the heading is kept.
    /// </summary>
    public void SetManualDirection(float x, float z)
    {
        m_manualDirection = MovementModel.NormalizeOrZero(x, z);
    }

    /// <summary>
    /// Starts walking to a point. False, with whatever the player was doing left alone, when no route exists.
    /// </summary>
    public bool TryMoveTo(WorldPosition from, WorldPosition destination)
    {
        if (!m_pathfinder.TryFindPath(from, destination, PathNodeBudget, m_foundWaypoints))
        {
            RejectedMoveRequests++;
            return false;
        }

        m_follower.Follow(m_foundWaypoints);
        return true;
    }

    public void CancelPath()
    {
        if (m_follower.IsActive)
        {
            CancelledPaths++;
        }

        m_follower.Cancel();
    }

    public WorldDirection Tick(WorldPosition position, float stepDistance)
    {
        if (m_manualDirection != default)
        {
            CancelPath();
            return m_manualDirection;
        }

        return m_follower.Advance(position, stepDistance);
    }
}
}
