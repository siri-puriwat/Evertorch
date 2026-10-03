using System;
using System.Globalization;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     Tells a boss's map of its appearance and its fall (Network Protocol §9), logs both, and counts the falls (System
///     Architecture §10). Only the tick thread calls it.
/// </summary>
public sealed class BossAnnouncer
{
    private static readonly Action<ILogger, string, long, string, Exception?> LogSpawned =
        LoggerMessage.Define<string, long, string>(
            LogLevel.Information,
            new EventId(1023, "BossSpawned"),
            "Boss {Monster} spawned as entity {Entity} on {Map}.");

    private static readonly Action<ILogger, string, long, string, Exception?> LogDefeated =
        LoggerMessage.Define<string, long, string>(
            LogLevel.Information,
            new EventId(1024, "BossDefeated"),
            "Boss {Monster} (entity {Entity}) was defeated; its most valuable player: {Character}.");

    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<BossAnnouncer> m_logger;

    public BossAnnouncer(
        SessionRegistry sessions,
        MessageSender sender,
        ServerInstruments instruments,
        ILogger<BossAnnouncer> logger)
    {
        m_sessions = sessions;
        m_sender = sender;
        m_instruments = instruments;
        m_logger = logger;
    }

    public void Appeared(MapInstance map, MonsterEntity boss)
    {
        LogSpawned(m_logger, boss.Definition.Id.Value, boss.Id.Value, map.Definition.Id.Value, null);
        Tell(map, new BossAnnouncement(BossAnnouncementKind.Appeared, boss.Definition.Id, string.Empty));
    }

    // The log names the most valuable player by its character's number alone; the players hear its name.
    public void Fell(MapInstance map, MonsterEntity boss, MostValuablePlayer? mostValuable)
    {
        m_instruments.RecordBossKill(boss.Definition.Id);
        string character = mostValuable != null
            ? mostValuable.Character.Character.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
        LogDefeated(m_logger, boss.Definition.Id.Value, boss.Id.Value, character, null);
        Tell(
            map,
            new BossAnnouncement(
                BossAnnouncementKind.Fell,
                boss.Definition.Id,
                mostValuable?.Character.Player.Name ?? string.Empty));

        // A prize is told of once it settles (BossRewardSystem); without one, the award is told at once.
        if (mostValuable != null && mostValuable.Prize == null)
        {
            m_sender.SendMvpAwarded(
                mostValuable.Character,
                new MvpAwarded(
                    boss.Definition.Id,
                    (ulong)mostValuable.GainedExperience,
                    null,
                    0,
                    PrizePlacement.None));
        }
    }

    // Every player with a connection in the world on the boss's map hears it, wherever on the map it stands.
    private void Tell(MapInstance map, BossAnnouncement message)
    {
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State == SessionState.InWorld && session.Map == map)
            {
                m_sender.Send(session.Connection, message);
            }
        }
    }
}
}
