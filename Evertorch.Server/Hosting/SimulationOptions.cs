namespace Evertorch.Server
{
public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";
    public const int DefaultTickRate = 20;
    public const int MinimumTickRate = 1;
    public const int MaximumTickRate = 120;
    public const int DefaultMaxCatchUpTicks = 5;
    public const int MaximumMaxCatchUpTicks = 100;

    public int TickRate { get; set; } = DefaultTickRate;

    /// <summary>
    /// How many steps the loop may fall behind and still simulate back to back before it abandons the backlog.
    /// </summary>
    public int MaxCatchUpTicks { get; set; } = DefaultMaxCatchUpTicks;
}
}
