using System;
using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
///     An immutable picture of the server taken by the tick thread. Everything outside the simulation that wants to
///     know how the server is doing reads one of these.
/// </summary>
public sealed class ServerStatus
{
    public ServerStatus(
        uint tick,
        int tickRate,
        TimeSpan publishedAt,
        TimeSpan lastTickDuration,
        TimeSpan maxTickDuration,
        long overruns,
        long skippedSteps,
        int inboundQueueDepth,
        long inboundDropped,
        long malformedMessages,
        long ignoredEvents,
        int connectedSessions,
        int authenticatedSessions,
        int inWorldSessions,
        long authenticationFailures,
        bool isAdmissionOpen,
        PersistenceStatus persistence,
        AbuseStatus abuse,
        TransportStatistics transport,
        IReadOnlyDictionary<string, int> playersPerMap,
        IReadOnlyDictionary<string, int> monstersPerMap,
        IReadOnlyDictionary<string, int> entitiesPerMap,
        IReadOnlyList<PlayerSummary> players)
    {
        Tick = tick;
        TickRate = tickRate;
        PublishedAt = publishedAt;
        LastTickDuration = lastTickDuration;
        MaxTickDuration = maxTickDuration;
        Overruns = overruns;
        SkippedSteps = skippedSteps;
        InboundQueueDepth = inboundQueueDepth;
        InboundDropped = inboundDropped;
        MalformedMessages = malformedMessages;
        IgnoredEvents = ignoredEvents;
        ConnectedSessions = connectedSessions;
        AuthenticatedSessions = authenticatedSessions;
        InWorldSessions = inWorldSessions;
        AuthenticationFailures = authenticationFailures;
        IsAdmissionOpen = isAdmissionOpen;
        Persistence = persistence;
        Abuse = abuse;
        Transport = transport;
        PlayersPerMap = playersPerMap;
        MonstersPerMap = monstersPerMap;
        EntitiesPerMap = entitiesPerMap;
        Players = players;
    }

    public static ServerStatus Empty { get; } = new(
        0,
        0,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        default,
        default,
        default,
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        new PlayerSummary[0]);

    public uint Tick { get; }

    public int TickRate { get; }

    /// <summary>
    ///     When this was published, on the server's monotonic clock, which starts with the host: the uptime then.
    /// </summary>
    public TimeSpan PublishedAt { get; }

    public TimeSpan LastTickDuration { get; }

    public TimeSpan MaxTickDuration { get; }

    public long Overruns { get; }

    public long SkippedSteps { get; }

    public int InboundQueueDepth { get; }

    public long InboundDropped { get; }

    public long MalformedMessages { get; }

    public long IgnoredEvents { get; }

    public int ConnectedSessions { get; }

    /// <summary>
    ///     Sessions signed in to an account, whether choosing a character, entering, or in the world.
    /// </summary>
    public int AuthenticatedSessions { get; }

    public int InWorldSessions { get; }

    public long AuthenticationFailures { get; }

    public bool IsAdmissionOpen { get; }

    public PersistenceStatus Persistence { get; }

    public AbuseStatus Abuse { get; }

    public TransportStatistics Transport { get; }

    public IReadOnlyDictionary<string, int> PlayersPerMap { get; }

    public IReadOnlyDictionary<string, int> MonstersPerMap { get; }

    public IReadOnlyDictionary<string, int> EntitiesPerMap { get; }

    public IReadOnlyList<PlayerSummary> Players { get; }
}
}
