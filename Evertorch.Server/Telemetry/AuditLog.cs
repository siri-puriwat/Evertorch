using System;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     The audit events, in their own category (System Architecture §10). Those of the abuse controls (Network Protocol
///     §11) are refusals at Debug, and violations and the disconnects they cause at Information; a connection writes at
///     most <see cref="EventsPerConnection" /> a second, and each event at most <see cref="EventsPerKind" /> a second
///     over all connections, while the meter keeps the exact counts. Connections, accounts, and characters appear only
///     as numbers: no event carries payload bytes, a token, an identity, or a connection string. Operator commands
///     are recorded at Information with their actor, and never held back.
/// </summary>
public sealed class AuditLog
{
    public const int EventsPerConnection = 10;
    public const int EventsPerKind = 100;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static readonly Action<ILogger, long, long, long, InboundEventKind, CommandRejectionReason, int, Exception?>
        LogCommandRefused = LoggerMessage.Define<long, long, long, InboundEventKind, CommandRejectionReason, int>(
            LogLevel.Debug,
            new EventId(5001, "CommandRefused"),
            "Connection {Connection} (account {Account}, character {Character}) had {Command} refused: {Reason} "
            + "({Suppressed} held back).");

    private static readonly Action<ILogger, long, long, long, InboundEventKind, string, int, Exception?>
        LogCommandThrottled = LoggerMessage.Define<long, long, long, InboundEventKind, string, int>(
            LogLevel.Debug,
            new EventId(5002, "CommandThrottled"),
            "Connection {Connection} (account {Account}, character {Character}) sent {Command} over the {Limit} "
            + "limit ({Suppressed} held back).");

    private static readonly Action<ILogger, long, long, long, Violation, double, int, Exception?> LogViolationScored =
        LoggerMessage.Define<long, long, long, Violation, double, int>(
            LogLevel.Information,
            new EventId(5003, "ViolationScored"),
            "Connection {Connection} (account {Account}, character {Character}) scored {Violation}; its score is "
            + "{Score} ({Suppressed} held back).");

    private static readonly Action<ILogger, long, long, long, DisconnectReason, double, int, Exception?>
        LogViolationDisconnect = LoggerMessage.Define<long, long, long, DisconnectReason, double, int>(
            LogLevel.Information,
            new EventId(5004, "ViolationDisconnect"),
            "Connection {Connection} (account {Account}, character {Character}) was closed with {Reason} at score "
            + "{Score} ({Suppressed} held back).");

    private static readonly Action<ILogger, long, long, string, int, Exception?> LogConnectionRefused =
        LoggerMessage.Define<long, long, string, int>(
            LogLevel.Debug,
            new EventId(5005, "ConnectionRefused"),
            "Connection {Connection} (account {Account}) was refused by the {Limit} limit ({Suppressed} held back).");

    private static readonly Action<ILogger, string, string, int, Exception?> LogOperatorSaved =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information,
            new EventId(6001, "OperatorSaved"),
            "Operator {Actor} ({User}) saved: {Queued} checkpoint(s) queued.");

    private static readonly Action<ILogger, string, string, string, Exception?> LogOperatorShutdown =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(6002, "OperatorShutdown"),
            "Operator {Actor} ({User}) shut the server down: \"{Reason}\".");

    private readonly ILogger m_logger;
    private readonly IMonotonicClock m_clock;
    private readonly RateLimitedLog m_commandsRefused;
    private readonly RateLimitedLog m_commandsThrottled;
    private readonly RateLimitedLog m_violations;
    private readonly RateLimitedLog m_disconnects;
    private readonly RateLimitedLog m_connectionsRefused;

    /// <param name="logger">A logger of the <see cref="LogCategories.Audit" /> category.</param>
    public AuditLog(ILogger logger, IMonotonicClock clock)
    {
        m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        m_clock = clock ?? throw new ArgumentNullException(nameof(clock));
        m_commandsRefused = new RateLimitedLog(clock, Interval, EventsPerKind);
        m_commandsThrottled = new RateLimitedLog(clock, Interval, EventsPerKind);
        m_violations = new RateLimitedLog(clock, Interval, EventsPerKind);
        m_disconnects = new RateLimitedLog(clock, Interval, EventsPerKind);
        m_connectionsRefused = new RateLimitedLog(clock, Interval, EventsPerKind);
    }

    public void CommandRefused(ClientSession session, InboundEventKind command, CommandRejectionReason reason)
    {
        if (m_logger.IsEnabled(LogLevel.Debug)
            && Admits(session)
            && m_commandsRefused.TryEnter(out int suppressed))
        {
            LogCommandRefused(
                m_logger,
                session.Connection.Value,
                AccountOf(session),
                CharacterOf(session),
                command,
                reason,
                suppressed,
                null);
        }
    }

    /// <param name="limit">The class of command whose bucket was empty (<see cref="ServerInstruments" />).</param>
    public void CommandThrottled(ClientSession session, InboundEventKind command, string limit)
    {
        if (m_logger.IsEnabled(LogLevel.Debug)
            && Admits(session)
            && m_commandsThrottled.TryEnter(out int suppressed))
        {
            LogCommandThrottled(
                m_logger,
                session.Connection.Value,
                AccountOf(session),
                CharacterOf(session),
                command,
                limit,
                suppressed,
                null);
        }
    }

    public void ViolationScored(ClientSession session, Violation violation, double score)
    {
        if (m_logger.IsEnabled(LogLevel.Information)
            && Admits(session)
            && m_violations.TryEnter(out int suppressed))
        {
            LogViolationScored(
                m_logger,
                session.Connection.Value,
                AccountOf(session),
                CharacterOf(session),
                violation,
                score,
                suppressed,
                null);
        }
    }

    // Once per connection, so only the cap over all connections applies.
    public void ViolationDisconnect(ClientSession session, DisconnectReason reason, double score)
    {
        if (m_logger.IsEnabled(LogLevel.Information) && m_disconnects.TryEnter(out int suppressed))
        {
            LogViolationDisconnect(
                m_logger,
                session.Connection.Value,
                AccountOf(session),
                CharacterOf(session),
                reason,
                score,
                suppressed,
                null);
        }
    }

    /// <summary>
    ///     A connection request or a sign-in refused by <paramref name="limit" />. A request refused before any
    ///     connection existed has connection 0, and one refused before sign-in has account 0. Safe on any thread.
    /// </summary>
    public void ConnectionRefused(ConnectionId connection, AccountId? account, string limit)
    {
        if (m_logger.IsEnabled(LogLevel.Debug) && m_connectionsRefused.TryEnter(out int suppressed))
        {
            LogConnectionRefused(m_logger, connection.Value, account?.Value ?? 0, limit, suppressed, null);
        }
    }

    public void OperatorSaved(AdminActor actor, int queued)
    {
        LogOperatorSaved(m_logger, actor.Name, actor.User, queued, null);
    }

    public void OperatorShutdown(AdminActor actor, string reason)
    {
        LogOperatorShutdown(m_logger, actor.Name, actor.User, reason, null);
    }

    private static long AccountOf(ClientSession session)
    {
        return session.Account?.Value ?? 0;
    }

    private static long CharacterOf(ClientSession session)
    {
        return session.Character?.Character.Value ?? 0;
    }

    // The session's own share of the log; tick thread only, like the session.
    private bool Admits(ClientSession session)
    {
        session.AuditLimit ??= new RateLimitedLog(m_clock, Interval, EventsPerConnection);
        return session.AuditLimit.TryEnter(out int _);
    }
}
}
