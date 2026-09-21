namespace Evertorch.Game
{
/// <summary>
/// Where one simulation step left a body, and how it actually moved to get there.
/// </summary>
public readonly struct MovementStep
{
    public MovementStep(
        WorldPosition position,
        WorldDirection facing,
        float velocityX,
        float velocityY,
        float velocityZ)
    {
        Position = position;
        Facing = facing;
        VelocityX = velocityX;
        VelocityY = velocityY;
        VelocityZ = velocityZ;
    }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    /// <summary>
    /// Displacement over the step divided by its duration, so it is zero against a wall whatever was requested.
    /// </summary>
    public float VelocityX { get; }

    public float VelocityY { get; }

    public float VelocityZ { get; }

    public bool IsMoving => VelocityX != 0f || VelocityY != 0f || VelocityZ != 0f;
}
}
