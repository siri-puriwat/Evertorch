using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     Owns the order in which the server's long-running parts start and stop.
/// </summary>
public sealed class ServerLifetimeService : IHostedService, IDisposable
{
    private static readonly Action<ILogger, Exception?> LogSimulationFaulted =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(1002, "SimulationFaulted"),
            "The simulation loop faulted; the server is stopping.");

    private static readonly Action<ILogger, string, string, int, Exception?> LogContentLoaded =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information,
            new EventId(1003, "ContentLoaded"),
            "Content loaded: server {ServerContentVersion}, client {ClientContentVersion}, {Maps} maps.");

    private readonly ServerContent m_content;
    private readonly IServerTransport m_transport;
    private readonly FixedStepLoop m_loop;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly ILogger<ServerLifetimeService> m_logger;
    private readonly CancellationTokenSource m_stop = new();
    private Thread? m_simulationThread;
    private volatile bool m_hasFaulted;
    private bool m_isDisposed;

    // The world is a parameter so that it exists, built from validated content, before anything can connect.
    public ServerLifetimeService(
        ServerContent content,
        WorldSimulation world,
        IServerTransport transport,
        FixedStepLoop loop,
        IHostApplicationLifetime lifetime,
        ILogger<ServerLifetimeService> logger)
    {
        _ = world;
        m_content = content;
        m_transport = transport;
        m_loop = loop;
        m_lifetime = lifetime;
        m_logger = logger;
    }

    public bool IsSimulationRunning => m_simulationThread != null && m_simulationThread.IsAlive;

    public bool HasFaulted => m_hasFaulted;

    public void Dispose()
    {
        // The container owns this instance through two registrations and disposes it once for each.
        if (m_isDisposed)
        {
            return;
        }

        m_isDisposed = true;

        // A host disposed without StopAsync must not leave a foreground thread simulating against disposed services.
        m_stop.Cancel();
        m_simulationThread?.Join();
        m_stop.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogContentLoaded(
            m_logger,
            m_content.ServerContentVersion,
            m_content.ClientContentVersion,
            m_content.Maps.Count,
            null);

        // A dedicated foreground thread keeps tick timing away from thread-pool starvation and keeps the process
        // alive until the current tick has finished.
        m_simulationThread = new Thread(RunSimulation)
        {
            Name = "Simulation",
            IsBackground = false
        };
        m_simulationThread.Start();

        // Last, so no client is ever admitted by a server that is not simulating yet.
        m_transport.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop admitting, let the tick in progress finish, then tell the remaining clients why they are dropped.
        m_transport.CloseAdmission();
        m_stop.Cancel();

        Thread? thread = m_simulationThread;
        if (thread != null)
        {
            await Task.Run(() => thread.Join(), CancellationToken.None)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        m_transport.Stop();
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
