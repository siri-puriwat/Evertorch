namespace Evertorch.Server
{
public readonly struct TickContext
{
    public TickContext(uint tick, float deltaSeconds)
    {
        Tick = tick;
        DeltaSeconds = deltaSeconds;
    }

    /// <summary>
    /// Number of the tick being simulated. The first tick is 1 and the number always advances by one.
    /// </summary>
    public uint Tick { get; }

    /// <summary>
    /// The fixed simulation step. It never reflects how long a tick actually took.
    /// </summary>
    public float DeltaSeconds { get; }
}
}
