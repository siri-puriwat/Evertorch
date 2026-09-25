using System;

namespace Evertorch.Client
{
/// <summary>
///     Where the camera sits around the player: its yaw from the map's north, its pitch above the ground, and its
///     distance, each kept within configured limits (Prototype Content §3). Client-only state that decides nothing on
///     the server; it lives on <see cref="GameClient" /> because every map scene brings its own camera and the client
///     keeps no mutable static state.
/// </summary>
public sealed class OrbitCameraState
{
    private const float StepTolerance = 0.01f;

    /// <summary>
    ///     The pitch of the fixed offset (0, 15, −11) the maps were first played with, so that the default view is
    ///     the one earlier tests aim their clicks through.
    /// </summary>
    public static readonly float DefaultPitchDegrees = (float)(Math.Atan2(15.0, 11.0) * 180.0 / Math.PI);

    /// <summary>
    ///     The length of that offset.
    /// </summary>
    public static readonly float DefaultDistance = (float)Math.Sqrt(15.0 * 15.0 + 11.0 * 11.0);

    private static readonly float[] ZoomSteps = { DefaultDistance, 12f, 6f };

    public OrbitCameraState(
        float maxYawDegrees,
        float minPitchDegrees,
        float maxPitchDegrees,
        float minDistance,
        float maxDistance)
    {
        if (!(maxYawDegrees >= 0f && maxYawDegrees <= 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxYawDegrees), maxYawDegrees, "must be from 0 to 180");
        }

        if (!(minPitchDegrees > 0f && minPitchDegrees <= maxPitchDegrees && maxPitchDegrees < 90f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minPitchDegrees),
                minPitchDegrees,
                "the pitch limits must lie above 0 and below 90, the lower one first");
        }

        if (!(minDistance > 0f && minDistance <= maxDistance && !float.IsInfinity(maxDistance)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minDistance),
                minDistance,
                "the distance limits must be positive and finite, the lower one first");
        }

        MaxYawDegrees = maxYawDegrees;
        MinPitchDegrees = minPitchDegrees;
        MaxPitchDegrees = maxPitchDegrees;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
        PitchDegrees = Clamp(DefaultPitchDegrees, minPitchDegrees, maxPitchDegrees);
        Distance = Clamp(DefaultDistance, minDistance, maxDistance);
    }

    public float MaxYawDegrees { get; }

    public float MinPitchDegrees { get; }

    public float MaxPitchDegrees { get; }

    public float MinDistance { get; }

    public float MaxDistance { get; }

    /// <summary>
    ///     Degrees from the map's north (+Z), positive turning the camera toward the west; 0 looks north.
    /// </summary>
    public float YawDegrees { get; private set; }

    /// <summary>
    ///     Degrees above the ground plane, looking down at the player.
    /// </summary>
    public float PitchDegrees { get; private set; }

    /// <summary>
    ///     Metres from the player's feet to the camera, before anything in the way pulls it in.
    /// </summary>
    public float Distance { get; private set; }

    public void Orbit(float yawDegrees, float pitchDegrees)
    {
        if (float.IsNaN(yawDegrees) || float.IsNaN(pitchDegrees))
        {
            return;
        }

        YawDegrees = Clamp(YawDegrees + yawDegrees, -MaxYawDegrees, MaxYawDegrees);
        PitchDegrees = Clamp(PitchDegrees + pitchDegrees, MinPitchDegrees, MaxPitchDegrees);
    }

    public void Zoom(float metres)
    {
        if (!float.IsNaN(metres))
        {
            Distance = Clamp(Distance + metres, MinDistance, MaxDistance);
        }
    }

    /// <summary>
    ///     A pinch: the distance times <paramref name="factor" />.
    /// </summary>
    public void ScaleDistance(float factor)
    {
        if (factor > 0f && !float.IsInfinity(factor))
        {
            Distance = Clamp(Distance * factor, MinDistance, MaxDistance);
        }
    }

    /// <summary>
    ///     One press of the zoom button: the next of the three steps nearer than the current distance, and from the
    ///     nearest back to the farthest.
    /// </summary>
    public void StepZoom()
    {
        float next = Clamp(ZoomSteps[0], MinDistance, MaxDistance);
        foreach (float step in ZoomSteps)
        {
            float clamped = Clamp(step, MinDistance, MaxDistance);
            if (clamped < Distance - StepTolerance)
            {
                next = clamped;
                break;
            }
        }

        Distance = next;
    }

    /// <summary>
    ///     The camera's offset from the player's feet: X east, Y up, Z north.
    /// </summary>
    public void GetOffset(out float x, out float y, out float z)
    {
        double yaw = YawDegrees * Math.PI / 180.0;
        double pitch = PitchDegrees * Math.PI / 180.0;
        double horizontal = Distance * Math.Cos(pitch);
        x = (float)(-horizontal * Math.Sin(yaw));
        y = (float)(Distance * Math.Sin(pitch));
        z = (float)(-horizontal * Math.Cos(yaw));
    }

    private static float Clamp(float value, float min, float max)
    {
        return value < min ? min : value > max ? max : value;
    }
}
}
