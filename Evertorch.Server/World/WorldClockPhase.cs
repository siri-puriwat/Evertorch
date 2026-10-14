using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Sets <see cref="WorldSimulation.NowMs" /> as a tick starts, before the commands drain: a load, a settle, and a
///     checkpoint queued in any phase read the same time as the systems that end and start effects in that tick.
///     Registered before every other phase.
/// </summary>
public sealed class WorldClockPhase : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private readonly WorldSimulation m_world;
    private readonly int m_tickRate;

    public WorldClockPhase(WorldSimulation world, IOptions<SimulationOptions> simulation)
    {
        m_world = world;
        m_tickRate = simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.DrainCommands;

    public void Execute(in TickContext context)
    {
        m_world.NowMs = (long)(context.Tick - 1) * MillisecondsPerSecond / m_tickRate;
    }
}
}
