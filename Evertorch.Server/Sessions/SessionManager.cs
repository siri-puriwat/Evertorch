using System;
using System.Collections.Generic;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
/// First phase of every tick: turns what arrived from the network since the last tick into session and world
/// changes. It is the only code that creates, advances, or closes a session.
/// </summary>
public sealed class SessionManager : ITickPhase
{
    private const int MillisecondsPerSecond = 1000;

    private static readonly Action<ILogger, long, DisconnectReason, Exception?> LogHandshakeRejected =
        LoggerMessage.Define<long, DisconnectReason>(
            LogLevel.Information,
            new EventId(2001, "HandshakeRejected"),
            "Connection {Connection} was refused: {Reason}.");

    private static readonly Action<ILogger, long, long, long, Exception?> LogWorldEntered =
        LoggerMessage.Define<long, long, long>(
            LogLevel.Information,
            new EventId(2002, "WorldEntered"),
            "Connection {Connection} entered the world as character {Character}, entity {Entity}.");

    private static readonly Action<ILogger, long, Exception?> LogSessionClosed =
        LoggerMessage.Define<long>(
            LogLevel.Information,
            new EventId(2003, "SessionClosed"),
            "Connection {Connection} closed.");

    private static readonly Action<ILogger, long, Exception?> LogSessionFaulted =
        LoggerMessage.Define<long>(
            LogLevel.Error,
            new EventId(2004, "SessionFaulted"),
            "Handling input from connection {Connection} failed; the connection was closed.");

    private readonly InboundQueue m_inbound;
    private readonly SessionRegistry m_sessions;
    private readonly HandshakeValidator m_handshake;
    private readonly WorldSimulation m_world;
    private readonly MessageSender m_sender;
    private readonly TimeProvider m_time;
    private readonly ILogger<SessionManager> m_logger;
    private readonly string m_serverBuildVersion;
    private readonly uint m_tickRate;
    private readonly uint m_handshakeTimeoutTicks;
    private readonly List<ClientSession> m_expired = new List<ClientSession>();

    public SessionManager(
        InboundQueue inbound,
        SessionRegistry sessions,
        HandshakeValidator handshake,
        WorldSimulation world,
        MessageSender sender,
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        IOptions<NetworkOptions> network,
        IOptions<CompatibilityOptions> compatibility,
        ILogger<SessionManager> logger)
    {
        m_inbound = inbound;
        m_sessions = sessions;
        m_handshake = handshake;
        m_world = world;
        m_sender = sender;
        m_time = time;
        m_logger = logger;
        m_serverBuildVersion = compatibility.Value.ServerBuildVersion;
        m_tickRate = (uint)simulation.Value.TickRate;

        long timeoutTicks = ((long)network.Value.HandshakeTimeoutMs * simulation.Value.TickRate) /
                            MillisecondsPerSecond;
        m_handshakeTimeoutTicks = (uint)Math.Max(1L, timeoutTicks);
    }

    public TickPhase Phase => TickPhase.DrainCommands;

    /// <summary>
    /// Inputs that arrived malformed, out of order for the session's state, or for a connection already closed.
    /// </summary>
    public long IgnoredEvents { get; private set; }

    public void Execute(in TickContext context)
    {
        while (m_inbound.TryDequeue(out InboundEvent inboundEvent))
        {
            try
            {
                Handle(inboundEvent, context.Tick);
            }
            catch (Exception exception)
            {
                // One peer's input must never take the tick down with it.
                LogSessionFaulted(m_logger, inboundEvent.Connection.Value, exception);
                if (m_sessions.TryGet(inboundEvent.Connection, out ClientSession? faulted) && faulted != null)
                {
                    Close(faulted, DisconnectReason.InternalError);
                }
            }
        }

        ExpireSilentConnections(context.Tick);
    }

    private void Handle(InboundEvent inboundEvent, uint tick)
    {
        if (inboundEvent.Kind == InboundEventKind.Connected)
        {
            m_sessions.Add(new ClientSession(inboundEvent.Connection, tick));
            return;
        }

        if (!m_sessions.TryGet(inboundEvent.Connection, out ClientSession? session) || session == null)
        {
            IgnoredEvents++;
            return;
        }

        switch (inboundEvent.Kind)
        {
            case InboundEventKind.Disconnected:
                Remove(session);
                break;
            case InboundEventKind.Hello:
                HandleHello(session, inboundEvent.Hello!);
                break;
            case InboundEventKind.EnterWorld:
                HandleEnterWorld(session, inboundEvent.EnterWorld, tick);
                break;
            default:
                IgnoredEvents++;
                break;
        }
    }

    private void HandleHello(ClientSession session, ClientHello hello)
    {
        if (session.State != SessionState.AwaitingHello)
        {
            IgnoredEvents++;
            return;
        }

        DisconnectReason refusal = m_handshake.Validate(hello);
        if (refusal != DisconnectReason.None)
        {
            LogHandshakeRejected(m_logger, session.Connection.Value, refusal, null);
            Close(session, refusal);
            return;
        }

        session.State = SessionState.Authenticated;
        m_sender.Send(
            session.Connection,
            new ServerHello(
                ProtocolConstants.ProtocolVersion,
                m_serverBuildVersion,
                m_handshake.RequiredClientContentVersion,
                m_tickRate,
                m_time.GetUtcNow().ToUnixTimeMilliseconds()));
    }

    private void HandleEnterWorld(ClientSession session, EnterWorldRequest request, uint tick)
    {
        if (session.State == SessionState.AwaitingHello)
        {
            LogHandshakeRejected(m_logger, session.Connection.Value, DisconnectReason.AuthenticationFailed, null);
            Close(session, DisconnectReason.AuthenticationFailed);
            return;
        }

        // Until characters are persisted any positive ID names a character; ownership checks arrive with accounts.
        if (session.State != SessionState.Authenticated || request.Character.Value <= 0)
        {
            IgnoredEvents++;
            return;
        }

        if (m_sessions.TryGetByCharacter(request.Character, out ClientSession? previous) && previous != null)
        {
            Close(previous, DisconnectReason.SessionReplaced);
        }

        PlayerEntity player = m_world.SpawnPlayer(request.Character, session.Connection, out MapInstance map);
        session.Player = player;
        session.Map = map;
        session.State = SessionState.InWorld;
        m_sessions.BindCharacter(session, request.Character);

        m_sender.Send(
            session.Connection,
            new WorldEntered(
                map.Definition.Id,
                map.InstanceNumber,
                player.Id,
                tick,
                player.Position,
                player.Facing,
                player.MovementSpeed));
        LogWorldEntered(m_logger, session.Connection.Value, request.Character.Value, player.Id.Value, null);
    }

    private void ExpireSilentConnections(uint tick)
    {
        m_expired.Clear();
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.AwaitingHello &&
                tick - session.ConnectedAtTick >= m_handshakeTimeoutTicks)
            {
                m_expired.Add(session);
            }
        }

        foreach (ClientSession session in m_expired)
        {
            LogHandshakeRejected(m_logger, session.Connection.Value, DisconnectReason.AuthenticationFailed, null);
            Close(session, DisconnectReason.AuthenticationFailed);
        }
    }

    private void Close(ClientSession session, DisconnectReason reason)
    {
        Remove(session);
        m_sender.Disconnect(session.Connection, reason);
    }

    private void Remove(ClientSession session)
    {
        if (session.Player != null && session.Map != null)
        {
            m_world.RemovePlayer(session.Map, session.Player);
        }

        m_sessions.Remove(session);
        LogSessionClosed(m_logger, session.Connection.Value, null);
    }
}
}
