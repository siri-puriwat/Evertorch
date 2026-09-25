using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     The host's meter, <c>Evertorch.Server</c> (System Architecture §10). Counters and histograms are recorded where
///     things happen; gauges read the status the tick thread publishes. Tags are fixed strings that never name a
///     connection, an account, or a character.
/// </summary>
public sealed class ServerInstruments
{
    public const string MeterName = "Evertorch.Server";

    public const string PeerInputLimit = "peer_input";
    public const string PeerControlLimit = "peer_control";
    public const string QueueFullLimit = "queue_full";
    public const string AddressRateLimit = "address_rate";
    public const string AddressConnectionsLimit = "address_connections";
    public const string AddressCooldownLimit = "address_cooldown";
    public const string AccountCooldownLimit = "account_cooldown";
    public const string CombatCommandLimit = "session_combat";
    public const string PickupCommandLimit = "session_pickup";
    public const string SessionCommandLimit = "session_session";
    public const string ResyncRequestLimit = "session_resync";
    public const string AdmissionLimit = "admission";

    private const string OperationTag = "operation";
    private const string OutcomeTag = "outcome";

    private static readonly KeyValuePair<string, object?> Received = new("direction", "received");
    private static readonly KeyValuePair<string, object?> Sent = new("direction", "sent");

    private static readonly KeyValuePair<string, object?>[] OutcomeTags =
    {
        new(OutcomeTag, "succeeded"),
        new(OutcomeTag, "unavailable"),
        new(OutcomeTag, "failed")
    };

    private static readonly KeyValuePair<string, object?>[] DatabaseStateTags =
    {
        new("state", "unknown"),
        new("state", "available"),
        new("state", "unavailable"),
        new("state", "pending_migrations")
    };

    // Indexed by Violation.
    private static readonly KeyValuePair<string, object?>[] ViolationTags =
    {
        new("violation", "malformed"),
        new("violation", "repeated_hello"),
        new("violation", "stale_command"),
        new("violation", "input_rate"),
        new("violation", "command_rate")
    };

    private static readonly KeyValuePair<string, object?> RateLimitedReason = new("reason", "rate_limited");
    private static readonly KeyValuePair<string, object?> KickedReason = new("reason", "kicked");

    private readonly Histogram<double> m_tickDuration;
    private readonly Counter<long> m_tickOverruns;
    private readonly Counter<long> m_skippedSteps;
    private readonly Counter<long> m_authenticationFailures;
    private readonly Histogram<double> m_jobDuration;
    private readonly Counter<long> m_jobFailures;
    private readonly Counter<long> m_retries;
    private readonly Histogram<int> m_roundTripTime;
    private readonly Counter<long> m_rateLimited;
    private readonly Counter<long> m_violations;
    private readonly Counter<long> m_violationDisconnects;
    private readonly Counter<long> m_experience;
    private readonly Counter<long> m_levelUps;

    public ServerInstruments(IMeterFactory meters)
    {
        Meter = (meters ?? throw new ArgumentNullException(nameof(meters))).Create(MeterName);
        m_tickDuration = Meter.CreateHistogram<double>(
            "evertorch.tick.duration",
            "ms",
            "Time the simulation spent on one tick.");
        m_tickOverruns = Meter.CreateCounter<long>(
            "evertorch.tick.overruns",
            "{tick}",
            "Ticks that took longer than one step.");
        m_skippedSteps = Meter.CreateCounter<long>(
            "evertorch.tick.skipped_steps",
            "{tick}",
            "Steps abandoned because the loop fell too far behind.");
        m_authenticationFailures = Meter.CreateCounter<long>(
            "evertorch.sessions.authentication_failures",
            "{connection}",
            "Connections refused because they did not sign in.");
        m_jobDuration = Meter.CreateHistogram<double>(
            "evertorch.persistence.job.duration",
            "ms",
            "Time one database job took on the writer, retries included.");
        m_jobFailures = Meter.CreateCounter<long>(
            "evertorch.persistence.job.failures",
            "{job}",
            "Database jobs that did not succeed.");
        m_retries = Meter.CreateCounter<long>(
            "evertorch.persistence.retries",
            "{retry}",
            "Retries of database work after a transient failure.");
        m_roundTripTime = Meter.CreateHistogram<int>(
            "evertorch.sessions.round_trip_time",
            "ms",
            "Round-trip time of each player in the world, sampled once a second.");
        m_rateLimited = Meter.CreateCounter<long>(
            "evertorch.abuse.rate_limited",
            "{message}",
            "Messages and connection requests a rate limit refused, by limit.");
        m_violations = Meter.CreateCounter<long>(
            "evertorch.abuse.violations",
            "{violation}",
            "Violations added to connections' scores, by kind.");
        m_violationDisconnects = Meter.CreateCounter<long>(
            "evertorch.abuse.disconnects",
            "{connection}",
            "Connections closed for violations or for rate excess, by reason.");
        m_experience = Meter.CreateCounter<long>(
            "evertorch.progression.experience",
            "{experience}",
            "Experience awarded to characters for the monsters they helped kill.");
        m_levelUps = Meter.CreateCounter<long>(
            "evertorch.progression.level_ups",
            "{level}",
            "Levels characters gained.");
    }

    public Meter Meter { get; }

    public void RecordTick(TimeSpan duration)
    {
        m_tickDuration.Record(duration.TotalMilliseconds);
    }

    public void RecordOverrun(int skippedSteps)
    {
        m_tickOverruns.Add(1);
        m_skippedSteps.Add(skippedSteps);
    }

    public void RecordAuthenticationFailure()
    {
        m_authenticationFailures.Add(1);
    }

    /// <param name="operation">One of the fixed operation names of the persistence jobs.</param>
    public void RecordJob(string operation, PersistenceOutcome outcome, TimeSpan duration)
    {
        var operationTag = new KeyValuePair<string, object?>(OperationTag, operation);
        KeyValuePair<string, object?> outcomeTag = OutcomeTags[(int)outcome];
        m_jobDuration.Record(duration.TotalMilliseconds, operationTag, outcomeTag);
        if (outcome != PersistenceOutcome.Succeeded)
        {
            m_jobFailures.Add(1, operationTag, outcomeTag);
        }
    }

    public void RecordRetry(string operation)
    {
        m_retries.Add(1, new KeyValuePair<string, object?>(OperationTag, operation));
    }

    public void RecordRoundTripTime(int milliseconds)
    {
        m_roundTripTime.Record(milliseconds);
    }

    /// <param name="limit">One of the limit names above.</param>
    public void RecordRateLimited(string limit)
    {
        m_rateLimited.Add(1, new KeyValuePair<string, object?>("limit", limit));
    }

    public void RecordViolations(Violation violation, int count)
    {
        m_violations.Add(count, ViolationTags[(int)violation]);
    }

    /// <param name="reason"><c>RateLimited</c> or <c>Kicked</c>.</param>
    public void RecordViolationDisconnect(DisconnectReason reason)
    {
        m_violationDisconnects.Add(1, reason == DisconnectReason.Kicked ? KickedReason : RateLimitedReason);
    }

    public void RecordExperience(long experience)
    {
        m_experience.Add(experience);
    }

    public void RecordLevelUps(int levels)
    {
        m_levelUps.Add(levels);
    }

    /// <summary>
    ///     Adds the gauges and totals read from the published status. Called once, by its publisher.
    /// </summary>
    public void ObserveStatus(Func<ServerStatus> status)
    {
        Meter.CreateObservableUpDownCounter(
            "evertorch.sessions.connected",
            () => status().ConnectedSessions,
            "{session}",
            "Connections with a session.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.sessions.authenticated",
            () => status().AuthenticatedSessions,
            "{session}",
            "Sessions signed in to an account.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.sessions.in_world",
            () => status().InWorldSessions,
            "{session}",
            "Sessions controlling a character in the world.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.map.players",
            () => PerMap(status().PlayersPerMap),
            "{player}",
            "Players in each map.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.map.entities",
            () => PerMap(status().EntitiesPerMap),
            "{entity}",
            "Entities in each map: players, monsters, and drops.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.inbound.depth",
            () => status().InboundQueueDepth,
            "{message}",
            "Decoded messages waiting for the next tick.");
        Meter.CreateObservableCounter(
            "evertorch.inbound.dropped",
            () => status().InboundDropped,
            "{message}",
            "Messages dropped because the inbound queue was full.");
        Meter.CreateObservableCounter(
            "evertorch.inbound.malformed",
            () => status().MalformedMessages,
            "{message}",
            "Messages that did not decode.");
        Meter.CreateObservableCounter(
            "evertorch.transport.bytes",
            () => Directions(status().Transport.BytesReceived, status().Transport.BytesSent),
            "By",
            "Bytes on the gameplay socket.");
        Meter.CreateObservableCounter(
            "evertorch.transport.packets",
            () => Directions(status().Transport.PacketsReceived, status().Transport.PacketsSent),
            "{packet}",
            "Packets on the gameplay socket.");
        Meter.CreateObservableCounter(
            "evertorch.transport.packets_lost",
            () => status().Transport.PacketsLost,
            "{packet}",
            "Reliable packets the transport had to resend.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.persistence.pending",
            () => status().Persistence.PendingJobs,
            "{job}",
            "Database jobs waiting for the writer, checkpoints not included.");
        Meter.CreateObservableUpDownCounter(
            "evertorch.persistence.waiting_checkpoints",
            () => status().Persistence.WaitingCheckpoints,
            "{checkpoint}",
            "Characters whose checkpoint waits for the writer.");
        Meter.CreateObservableGauge(
            "evertorch.persistence.database.state",
            () => DatabaseStates(status().Persistence.State),
            "1",
            "1 for the state the server believes its database is in, 0 for the others.");
        Meter.CreateObservableGauge(
            "evertorch.admission.open",
            () => status().IsAdmissionOpen ? 1 : 0,
            "1",
            "1 while new connections are admitted.");
    }

    private static IEnumerable<Measurement<int>> PerMap(IReadOnlyDictionary<string, int> counts)
    {
        foreach (KeyValuePair<string, int> map in counts)
        {
            yield return new Measurement<int>(map.Value, new KeyValuePair<string, object?>("map", map.Key));
        }
    }

    private static IEnumerable<Measurement<long>> Directions(long received, long sent)
    {
        yield return new Measurement<long>(received, Received);
        yield return new Measurement<long>(sent, Sent);
    }

    private static IEnumerable<Measurement<int>> DatabaseStates(DatabaseState current)
    {
        for (int state = 0; state < DatabaseStateTags.Length; state++)
        {
            yield return new Measurement<int>(state == (int)current ? 1 : 0, DatabaseStateTags[state]);
        }
    }
}
}
