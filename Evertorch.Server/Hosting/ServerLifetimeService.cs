using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    private static readonly Action<ILogger, ulong, string, Exception?> LogRandomSeed =
        LoggerMessage.Define<ulong, string>(
            LogLevel.Information,
            new EventId(1004, "RandomSeedChosen"),
            "Random seed {Seed} ({Origin}).");

    private static readonly Action<ILogger, int, Exception?> LogSimulationStopTimedOut =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(1005, "SimulationStopTimedOut"),
            "The simulation thread did not stop within {TimeoutMs} ms; the world was not checkpointed.");

    // Room for the transport's stop and the other hosted services, beyond the join and the drain.
    private static readonly TimeSpan StopAllowance = TimeSpan.FromSeconds(5);

    private readonly ServerContent m_content;
    private readonly ServerRandom m_random;
    private readonly IServerTransport m_transport;
    private readonly PersistenceWorker m_persistence;
    private readonly CharacterLifetime m_characters;
    private readonly ShutdownRequest m_shutdown;
    private readonly int m_drainTimeoutMs;
    private readonly int m_joinTimeoutMs;
    private readonly FixedStepLoop m_loop;
    private readonly IHostApplicationLifetime m_lifetime;
    private readonly ILogger<ServerLifetimeService> m_logger;
    private readonly CancellationTokenSource m_stop = new();
    private Thread? m_simulationThread;
    private volatile bool m_hasFaulted;
    private bool m_hasTimedOut;
    private bool m_isDisposed;

    // The world is a parameter so that it exists, built from validated content, before anything can connect.
    public ServerLifetimeService(
        ServerContent content,
        WorldSimulation world,
        ServerRandom random,
        IServerTransport transport,
        PersistenceWorker persistence,
        CharacterLifetime characters,
        ShutdownRequest shutdown,
        IOptions<PersistenceOptions> persistenceOptions,
        FixedStepLoop loop,
        IHostApplicationLifetime lifetime,
        ILogger<ServerLifetimeService> logger)
    {
        _ = world;
        m_content = content;
        m_random = random;
        m_transport = transport;
        m_persistence = persistence;
        m_characters = characters;
        m_shutdown = shutdown;
        m_drainTimeoutMs = persistenceOptions.Value.CommandTimeoutMs;
        m_joinTimeoutMs = persistenceOptions.Value.CommandTimeoutMs;
        m_loop = loop;
        m_lifetime = lifetime;
        m_logger = logger;
    }

    public bool IsSimulationRunning => m_simulationThread != null && m_simulationThread.IsAlive;

    public bool HasFaulted => m_hasFaulted;

    /// <summary>
    ///     The simulation thread was still running when stopping stopped waiting for it.
    /// </summary>
    public bool HasTimedOut => m_hasTimedOut;

    /// <summary>
    ///     The simulation faulted or would not stop, so the process exits with code 1 and clients were told
    ///     <c>InternalError</c>.
    /// </summary>
    public bool HasFailed => m_hasFaulted || m_hasTimedOut;

    public void Dispose()
    {
        // The container owns this instance through two registrations and disposes it once for each.
        if (m_isDisposed)
        {
            return;
        }

        m_isDisposed = true;

        // A host disposed without StopAsync must not leave a thread simulating against disposed services. A thread
        // that will not stop keeps the token it is still reading.
        m_stop.Cancel();
        if (m_simulationThread == null || m_simulationThread.Join(m_joinTimeoutMs))
        {
            m_stop.Dispose();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogContentLoaded(
            m_logger,
            m_content.ServerContentVersion,
            m_content.ClientContentVersion,
            m_content.Maps.Count,
            null);
        LogRandomSeed(m_logger, m_random.Seed, m_random.IsConfigured ? "configured" : "drawn at startup", null);
        m_persistence.Start();

        // A dedicated thread keeps tick timing away from thread-pool starvation. It is a background thread: stopping
        // waits for the current tick with a bound, and a tick that never ends must not keep the process alive.
        m_simulationThread = new Thread(RunSimulation)
        {
            Name = "Simulation",
            IsBackground = true
        };
        m_simulationThread.Start();

        // Last, so no client is ever admitted by a server that is not simulating yet.
        m_transport.Start();
        return Task.CompletedTask;
    }

    // Stop admitting and let the tick in progress finish, then checkpoint the world only if the thread ended without
    // a fault, since otherwise the world is unknown or still changing. The drain and the notices always follow
    // (System Architecture §13). Each step has its own bound, so the host's token is not observed.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_transport.CloseAdmission();
        m_stop.Cancel();
        try
        {
            if (JoinSimulation() && !m_hasFaulted)
            {
                m_characters.CheckpointAll();
            }
        }
        finally
        {
            m_persistence.Stop(m_drainTimeoutMs);
            if (HasFailed)
            {
                m_transport.Stop(DisconnectReason.InternalError, string.Empty);
            }
            else
            {
                m_transport.Stop(DisconnectReason.Maintenance, m_shutdown.Message);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The host's shutdown timeout: above the bounds of the join and the drain together, so the host's token never
    ///     cuts the shutdown order short (System Architecture §13).
    /// </summary>
    public static TimeSpan ShutdownTimeout(PersistenceOptions options)
    {
        return TimeSpan.FromMilliseconds(2L * options.CommandTimeoutMs) + StopAllowance;
    }

    private bool JoinSimulation()
    {
        Thread? thread = m_simulationThread;
        if (thread == null || thread.Join(m_joinTimeoutMs))
        {
            return true;
        }

        m_hasTimedOut = true;
        LogSimulationStopTimedOut(m_logger, m_joinTimeoutMs, null);
        return false;
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
