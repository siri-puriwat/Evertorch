using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Client
{
/// <summary>
///     Turns a list of waypoints into one direction per client tick. It only steers; the shared movement model and the
///     server still decide where the body ends up.
/// </summary>
public sealed class PathFollower
{
    public const float MinimumArrivalDistance = 0.05f;

    // Half a second at 20 Hz without progress means something blocks the route the client thought was free.
    public const int StuckTickLimit = 10;

    private const float ProgressEpsilon = 0.001f;

    private readonly List<WorldPosition> m_waypoints = new();
    private int m_next;
    private int m_stuckTicks;
    private bool m_hasLastPosition;
    private WorldPosition m_lastPosition;

    public bool IsActive => m_next < m_waypoints.Count;

    public IReadOnlyList<WorldPosition> Waypoints => m_waypoints;

    public int NextWaypointIndex => m_next;

    public void Follow(IReadOnlyList<WorldPosition> waypoints)
    {
        if (waypoints == null)
        {
            throw new ArgumentNullException(nameof(waypoints));
        }

        Cancel();
        m_waypoints.AddRange(waypoints);
    }

    public void Cancel()
    {
        m_waypoints.Clear();
        m_next = 0;
        m_stuckTicks = 0;
        m_hasLastPosition = false;
    }

    /// <summary>
    ///     The direction for this tick, or zero once the last waypoint is reached or the body has stopped making
    ///     progress.
    /// </summary>
    /// <param name="stepDistance">
    ///     How far one tick moves the body. Speed is never scaled down, so a waypoint closer than half a step counts as
    ///     reached; stepping again would only overshoot it.
    /// </param>
    public WorldDirection Advance(WorldPosition position, float stepDistance)
    {
        if (!IsActive)
        {
            return default;
        }

        if (IsStuck(position))
        {
            Cancel();
            return default;
        }

        float arrival = Math.Max(MinimumArrivalDistance, stepDistance * 0.5f);
        while (IsActive)
        {
            WorldPosition waypoint = m_waypoints[m_next];
            float deltaX = waypoint.X - position.X;
            float deltaZ = waypoint.Z - position.Z;
            if (deltaX * deltaX + deltaZ * deltaZ > arrival * arrival)
            {
                return MovementModel.NormalizeOrZero(deltaX, deltaZ);
            }

            m_next++;
        }

        Cancel();
        return default;
    }

    private bool IsStuck(WorldPosition position)
    {
        if (m_hasLastPosition)
        {
            float deltaX = position.X - m_lastPosition.X;
            float deltaZ = position.Z - m_lastPosition.Z;
            bool hasMoved = deltaX * deltaX + deltaZ * deltaZ > ProgressEpsilon * ProgressEpsilon;
            m_stuckTicks = hasMoved ? 0 : m_stuckTicks + 1;
        }

        m_hasLastPosition = true;
        m_lastPosition = position;
        return m_stuckTicks >= StuckTickLimit;
    }
}
}
