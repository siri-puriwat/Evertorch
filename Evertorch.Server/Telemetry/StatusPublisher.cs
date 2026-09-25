using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Last phase of a tick. Once a second it copies what operators may see into an immutable <see cref="ServerStatus" />
///     and publishes it, so nothing outside the tick thread ever reads live sessions or the world.
/// </summary>
public sealed class StatusPublisher : ITickPhase
{
    private readonly ServerMetrics m_metrics;
    private readonly InboundQueue m_inbound;
    private readonly SessionRegistry m_sessions;
    private readonly SessionManager m_sessionManager;
    private readonly WorldSimulation m_world;
    private readonly IServerTransport m_transport;
    private readonly PersistenceWorker m_persistence;
    private readonly IMonotonicClock m_clock;
    private readonly ServerInstruments m_instruments;
    private readonly int m_tickRate;
    private ServerStatus m_current = ServerStatus.Empty;

    public StatusPublisher(
        ServerMetrics metrics,
        InboundQueue inbound,
        SessionRegistry sessions,
        SessionManager sessionManager,
        WorldSimulation world,
        IServerTransport transport,
        PersistenceWorker persistence,
        IMonotonicClock clock,
        ServerInstruments instruments,
        IOptions<SimulationOptions> simulation)
    {
        m_metrics = metrics;
        m_inbound = inbound;
        m_sessions = sessions;
        m_sessionManager = sessionManager;
        m_world = world;
        m_transport = transport;
        m_persistence = persistence;
        m_clock = clock;
        m_instruments = instruments;
        m_tickRate = simulation.Value.TickRate;
        instruments.ObserveStatus(() => Current);
    }

    /// <summary>
    ///     The most recent status. Safe to read from any thread.
    /// </summary>
    public ServerStatus Current => Volatile.Read(ref m_current);

    public TickPhase Phase => TickPhase.SchedulePersistence;

    public void Execute(in TickContext context)
    {
        // The first tick publishes too, so a status asked for right after start-up is not empty.
        if (context.Tick == 1 || context.Tick % (uint)m_tickRate == 0)
        {
            Volatile.Write(ref m_current, Build(context.Tick));
        }
    }

    private ServerStatus Build(uint tick)
    {
        var playersPerMap = new Dictionary<string, int>();
        var monstersPerMap = new Dictionary<string, int>();
        var entitiesPerMap = new Dictionary<string, int>();
        foreach (MapInstance map in m_world.Maps)
        {
            playersPerMap[map.Definition.Id.Value] = map.Players.Count;
            monstersPerMap[map.Definition.Id.Value] = map.Monsters.Count;
            entitiesPerMap[map.Definition.Id.Value] = map.Entities.Count;
        }

        var players = new List<PlayerSummary>();
        int authenticated = 0;
        int inWorld = 0;
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.Account != null)
            {
                authenticated++;
            }

            if (session.State != SessionState.InWorld
                || session.Player == null
                || session.Map == null
                || session.Input == null)
            {
                continue;
            }

            inWorld++;
            int roundTrip = -1;
            if (m_transport.TryGetRoundTripTime(session.Connection, out int milliseconds))
            {
                roundTrip = milliseconds;
                m_instruments.RecordRoundTripTime(milliseconds);
            }

            players.Add(
                new PlayerSummary(
                    session.Connection,
                    session.Player.Character,
                    session.Player.Id,
                    session.Map.Definition.Id,
                    session.Player.Position,
                    roundTrip,
                    session.Input.Queue.Count,
                    session.Input.Queue.Stale,
                    session.Input.Queue.Dropped,
                    session.RefusedCommands,
                    session.Player.Level,
                    session.Player.Experience));
        }

        return new ServerStatus(
            tick,
            m_tickRate,
            m_clock.Elapsed,
            m_metrics.LastTickDuration,
            m_metrics.MaxTickDuration,
            m_metrics.Overruns,
            m_metrics.SkippedSteps,
            m_inbound.Count,
            m_inbound.Dropped,
            m_inbound.Malformed,
            m_sessionManager.IgnoredEvents,
            m_sessions.Sessions.Count,
            authenticated,
            inWorld,
            m_sessionManager.AuthenticationFailures,
            m_transport.IsAdmissionOpen,
            new PersistenceStatus(
                m_persistence.State,
                m_persistence.PendingJobs,
                m_persistence.WaitingCheckpoints,
                m_persistence.Retries),
            new AbuseStatus(
                m_inbound.OverBudget,
                m_sessionManager.ThrottledCommands,
                m_sessionManager.Violations,
                m_sessionManager.ViolationDisconnects),
            m_transport.GetStatistics(),
            playersPerMap,
            monstersPerMap,
            entitiesPerMap,
            players);
    }
}
}
