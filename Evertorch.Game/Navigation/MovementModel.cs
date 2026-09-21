using System;

namespace Evertorch.Game
{
/// <summary>
/// The movement arithmetic the server applies and the client predicts with. Both sides must call these functions
/// rather than reimplement them, or prediction and authority drift apart.
/// </summary>
public static class MovementModel
{
    /// <summary>
    /// Requested directions shorter than this count as no movement.
    /// </summary>
    public const float MinimumDirectionMagnitude = 0.001f;

    /// <summary>
    /// Longest distance one step may cover. Anything larger is a broken speed or duration, not movement.
    /// </summary>
    public const float MaxStepDistance = 256f;

    /// <summary>
    /// Turns a requested direction into a unit vector, or zero when it is too short or not finite. The length of a
    /// request never scales speed: half-tilting a stick moves as fast as pushing it fully.
    /// </summary>
    public static WorldDirection NormalizeOrZero(float x, float z)
    {
        // Doubles keep the squares of very large floats finite.
        double magnitude = Math.Sqrt(((double)x * x) + ((double)z * z));
        if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude < MinimumDirectionMagnitude)
        {
            return new WorldDirection(0f, 0f);
        }

        return new WorldDirection((float)(x / magnitude), (float)(z / magnitude));
    }

    /// <summary>
    /// Advances a body by one step of <paramref name="deltaSeconds"/> at <paramref name="speed"/> world units per
    /// second. The move is split into pieces no longer than <see cref="NavigationGrid.MaxMoveStep"/>; a piece that
    /// is blocked is retried along X alone and then Z alone, so a body slides along walls instead of sticking.
    /// A position the grid rejects is never returned: the worst case is staying put.
    /// </summary>
    public static MovementStep Step(
        NavigationGrid grid,
        WorldPosition position,
        WorldDirection facing,
        WorldDirection requestedDirection,
        float speed,
        float deltaSeconds)
    {
        if (grid == null)
        {
            throw new ArgumentNullException(nameof(grid));
        }

        WorldDirection direction = NormalizeOrZero(requestedDirection.X, requestedDirection.Z);
        float distance = speed * deltaSeconds;
        bool canMove = (direction.X != 0f || direction.Z != 0f) && distance > 0f && distance <= MaxStepDistance;
        if (!canMove)
        {
            return new MovementStep(position, facing, 0f, 0f, 0f);
        }

        float x = position.X;
        float z = position.Z;
        int pieces = (int)Math.Ceiling(distance / grid.MoveStepLength);
        float pieceLength = distance / pieces;
        for (int piece = 0; piece < pieces; piece++)
        {
            float stepX = direction.X * pieceLength;
            float stepZ = direction.Z * pieceLength;
            if (grid.CanStep(x, z, x + stepX, z + stepZ))
            {
                x += stepX;
                z += stepZ;
            }
            else if (stepX != 0f && grid.CanStep(x, z, x + stepX, z))
            {
                x += stepX;
            }
            else if (stepZ != 0f && grid.CanStep(x, z, x, z + stepZ))
            {
                z += stepZ;
            }
            else
            {
                break;
            }
        }

        float y = grid.TrySampleHeight(x, z, out float ground) ? ground : position.Y;
        return new MovementStep(
            new WorldPosition(x, y, z),
            direction,
            (x - position.X) / deltaSeconds,
            (y - position.Y) / deltaSeconds,
            (z - position.Z) / deltaSeconds);
    }
}
}
