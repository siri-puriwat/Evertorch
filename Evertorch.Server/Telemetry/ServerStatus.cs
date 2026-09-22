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
        TimeSpan lastTickDuration,
        TimeSpan maxTickDuration,
        long overruns,
        long skippedSteps,
        int inboundQueueDepth,
        long inboundDropped,
        long malformedMessages,
        long ignoredEvents,
        int connectedSessions,
        int inWorldSessions,
        TransportStatistics transport,
        IReadOnlyDictionary<string, int> playersPerMap,
        IReadOnlyList<PlayerSummary> players)
    {
        Tick = tick;
        TickRate = tickRate;
        LastTickDuration = lastTickDuration;
        MaxTickDuration = maxTickDuration;
        Overruns = overruns;
        SkippedSteps = skippedSteps;
        InboundQueueDepth = inboundQueueDepth;
        InboundDropped = inboundDropped;
        MalformedMessages = malformedMessages;
        IgnoredEvents = ignoredEvents;
        ConnectedSessions = connectedSessions;
        InWorldSessions = inWorldSessions;
        Transport = transport;
        PlayersPerMap = playersPerMap;
        Players = players;
    }

    public static ServerStatus Empty { get; } = new(
        0,
        0,
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
        default,
        new Dictionary<string, int>(),
        new PlayerSummary[0]);

    public uint Tick { get; }

    public int TickRate { get; }

    public TimeSpan LastTickDuration { get; }

    public TimeSpan MaxTickDuration { get; }

    public long Overruns { get; }

    public long SkippedSteps { get; }

    public int InboundQueueDepth { get; }

    public long InboundDropped { get; }

    public long MalformedMessages { get; }

    public long IgnoredEvents { get; }

    public int ConnectedSessions { get; }

    public int InWorldSessions { get; }

    public TransportStatistics Transport { get; }

    public IReadOnlyDictionary<string, int> PlayersPerMap { get; }

    public IReadOnlyList<PlayerSummary> Players { get; }
}
}
