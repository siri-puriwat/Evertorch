using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Chooses this tick's direction from the ways a player can ask to move: a held direction, a point to walk to,
///     or a target to walk up to. A held direction always wins and ends the walk in the same tick it is seen. While
///     a swing is committed the direction is zero; a walk requested meanwhile starts after the impact.
/// </summary>
public sealed class MovementController
{
    public const int PathNodeBudget = 8192;

    /// <summary>
    ///     How far a chased target may move from where its path was aimed before the path is found again.
    /// </summary>
    public const float ChaseRepathDistance = 0.5f;

    private readonly GridPathfinder m_pathfinder;
    private readonly PathFollower m_follower = new();
    private readonly List<WorldPosition> m_foundWaypoints = new();
    private WorldDirection m_manualDirection;
    private WorldPosition m_chaseGoal;

    public MovementController(NavigationGrid grid)
    {
        m_pathfinder = new GridPathfinder(grid ?? throw new ArgumentNullException(nameof(grid)));
    }

    public bool HasPath => m_follower.IsActive;

    public IReadOnlyList<WorldPosition> Path => m_follower.Waypoints;

    public int NextWaypointIndex => m_follower.NextWaypointIndex;

    public int RejectedMoveRequests { get; private set; }

    public int CancelledPaths { get; private set; }

    public bool HasManualDirection => m_manualDirection != default;

    /// <summary>
    ///     Whether the active path is an approach to a target rather than a walk the player asked for.
    /// </summary>
    public bool IsChasing { get; private set; }

    /// <summary>
    ///     Set from a swing's start until its impact (Gameplay Systems §7): the server applies movement as zero then,
    ///     and so does the prediction.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    ///     The latest held direction in world space. Any length is accepted; only the heading is kept.
    /// </summary>
    public void SetManualDirection(float x, float z)
    {
        m_manualDirection = MovementModel.NormalizeOrZero(x, z);
    }

    /// <summary>
    ///     Starts walking to a point. False, with whatever the player was doing left alone, when no route exists.
    /// </summary>
    public bool TryMoveTo(WorldPosition from, WorldPosition destination)
    {
        if (!m_pathfinder.TryFindPath(from, destination, PathNodeBudget, m_foundWaypoints))
        {
            RejectedMoveRequests++;
            return false;
        }

        m_follower.Follow(m_foundWaypoints);
        IsChasing = false;
        return true;
    }

    /// <summary>
    ///     Walks toward a target's drawn position, finding the path again only when the target has moved on. The
    ///     approach produces ordinary movement intents; the server only checks range.
    /// </summary>
    public void Chase(WorldPosition from, WorldPosition target)
    {
        if (IsChasing && m_follower.IsActive && HorizontalDistance(m_chaseGoal, target) <= ChaseRepathDistance)
        {
            return;
        }

        if (m_pathfinder.TryFindPath(from, target, PathNodeBudget, m_foundWaypoints))
        {
            m_follower.Follow(m_foundWaypoints);
            m_chaseGoal = target;
            IsChasing = true;
        }
    }

    /// <summary>
    ///     Ends an approach that has arrived; a walk the player asked for is left alone.
    /// </summary>
    public void StopChase()
    {
        if (IsChasing)
        {
            m_follower.Cancel();
            IsChasing = false;
        }
    }

    public void CancelPath()
    {
        if (m_follower.IsActive)
        {
            CancelledPaths++;
        }

        m_follower.Cancel();
        IsChasing = false;
    }

    public WorldDirection Tick(WorldPosition position, float stepDistance)
    {
        if (m_manualDirection != default)
        {
            CancelPath();
            return IsLocked ? default : m_manualDirection;
        }

        return IsLocked ? default : m_follower.Advance(position, stepDistance);
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
}
