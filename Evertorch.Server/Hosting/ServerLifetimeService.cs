using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
/// Owns the order in which the server's long-running parts start and stop.
/// </summary>
public sealed class ServerLifetimeService : IHostedService, IDisposable
{
    private static readonly Action<ILogger, Exception?> LogSimulationFaulted =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(1002, "SimulationFaulted"),
            "The simulation loop faulted; the server is stopping.");

    private readonly FixedStepLoop m_loop;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly ILogger<ServerLifetimeService> m_logger;
    private readonly CancellationTokenSource m_stop = new CancellationTokenSource();
    private Thread? m_simulationThread;
    private volatile bool m_hasFaulted;

    public ServerLifetimeService(
        FixedStepLoop loop,
        IHostApplicationLifetime lifetime,
        ILogger<ServerLifetimeService> logger)
    {
        m_loop = loop;
        m_lifetime = lifetime;
        m_logger = logger;
    }

    public bool IsSimulationRunning => m_simulationThread != null && m_simulationThread.IsAlive;

    public bool HasFaulted => m_hasFaulted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A dedicated foreground thread keeps tick timing away from thread-pool starvation and keeps the process
        // alive until the current tick has finished.
        m_simulationThread = new Thread(RunSimulation)
        {
            Name = "Simulation",
            IsBackground = false,
        };
        m_simulationThread.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        m_stop.Cancel();

        Thread? thread = m_simulationThread;
        if (thread == null)
        {
            return;
        }

        await Task.Run(() => thread.Join(), CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        m_stop.Dispose();
    }

    private void RunSimulation()
    {
        try
        {
            m_loop.Run(m_stop.Token);
        }
        catch (Exception exception)
        {
            // Nothing above this frame can handle it, and a server without a simulation must not keep accepting
            // players, so the whole host goes down.
            LogSimulationFaulted(m_logger, exception);
            m_hasFaulted = true;
            m_lifetime.StopApplication();
        }
    }
}
}
