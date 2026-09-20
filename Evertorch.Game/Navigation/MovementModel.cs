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
}
}
