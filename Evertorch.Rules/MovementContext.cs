namespace Evertorch.Rules
{
public readonly struct MovementContext
{
    public MovementContext(float baseSpeed)
    {
        BaseSpeed = baseSpeed;
    }

    /// <summary>World units per second before any rule is applied.</summary>
    public float BaseSpeed { get; }
}
}
