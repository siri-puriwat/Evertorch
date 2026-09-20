namespace Evertorch.Rules
{
public readonly struct MovementParameters
{
    public MovementParameters(float speed)
    {
        Speed = speed;
    }

    /// <summary>Authoritative world units per second.</summary>
    public float Speed { get; }
}
}
