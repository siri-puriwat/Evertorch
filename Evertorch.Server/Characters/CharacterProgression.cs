using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     Shares a dead monster's experience among the characters that damaged it and levels them up (Gameplay Systems
///     §2.1). Tick thread only.
/// </summary>
public sealed class CharacterProgression
{
    private static readonly Action<ILogger, long, long, int, int, Exception?> LogLeveledUp =
        LoggerMessage.Define<long, long, int, int>(
            LogLevel.Information,
            new EventId(1006, "CharacterLeveledUp"),
            "Character {Character} on connection {Connection} reached level {Level} from level {PreviousLevel}.");

    private readonly SessionRegistry m_sessions;
    private readonly CharacterLifetime m_lifetime;
    private readonly CharacterStats m_stats;
    private readonly IProgressionRules m_rules;
    private readonly ServerContent m_content;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<CharacterProgression> m_logger;

    public CharacterProgression(
        SessionRegistry sessions,
        CharacterLifetime lifetime,
        CharacterStats stats,
        IProgressionRules rules,
        ServerContent content,
        MessageSender sender,
        ServerInstruments instruments,
        ILogger<CharacterProgression> logger)
    {
        m_sessions = sessions;
        m_lifetime = lifetime;
        m_stats = stats;
        m_rules = rules;
        m_content = content;
        m_sender = sender;
        m_instruments = instruments;
        m_logger = logger;
    }

    /// <summary>
    ///     What <paramref name="player" />'s next level needs in all; 0 at its job's level cap.
    /// </summary>
    public long ExperienceToNextLevel(PlayerEntity player)
    {
        return m_rules.ExperienceToNextLevel(TableOf(player), player.Level);
    }

    /// <summary>
    ///     Awards <paramref name="monster" />'s base experience, which has just died on <paramref name="map" />: each
    ///     character in its damage log that is alive on the map, not logging out, and not expelled gets its share of
    ///     the whole log. A character in its reconnect grace period is still on the map and shares.
    /// </summary>
    public void AwardKill(MapInstance map, MonsterEntity monster)
    {
        IReadOnlyList<DamageLogEntry> log = monster.DamageLog;
        long baseExperience = monster.Definition.BaseExperience;
        if (baseExperience <= 0 || log.Count == 0)
        {
            return;
        }

        long total = 0;
        foreach (DamageLogEntry entry in log)
        {
            total += entry.Damage;
        }

        foreach (DamageLogEntry entry in log)
        {
            if (m_sessions.TryGetCharacter(entry.Character, out CharacterSession? character)
                && character != null
                && character.Map == map
                && !character.Player.IsDead
                && !character.IsLoggingOut
                && !character.IsExpelled)
            {
                Award(character, m_rules.ShareExperience(baseExperience, entry.Damage, total));
            }
        }
    }

    private void Award(CharacterSession character, long experience)
    {
        PlayerEntity player = character.Player;
        int previousLevel = player.Level;
        LevelProgress progress = m_rules.AddExperience(
            TableOf(player),
            new LevelProgress(previousLevel, player.Experience),
            experience);
        player.Experience = progress.Experience;
        m_instruments.RecordExperience(experience);
        if (progress.Level != previousLevel)
        {
            LevelUp(character, progress.Level, previousLevel);
        }

        if (player.Owner != default)
        {
            m_sender.Send(
                player.Owner,
                new CharacterProgress(
                    (ushort)player.Level,
                    (ulong)player.Experience,
                    (ulong)ExperienceToNextLevel(player)));
        }
    }

    private void LevelUp(CharacterSession character, int level, int previousLevel)
    {
        PlayerEntity player = character.Player;

        // A level-up restores HP and SP in full, as the reference does.
        player.Level = level;
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        player.CurrentHealth = player.MaxHealth;
        player.CurrentSpirit = player.MaxSpirit;
        m_instruments.RecordLevelUps(level - previousLevel);
        LogLeveledUp(
            m_logger,
            character.Character.Value,
            character.Connection?.Connection.Value ?? 0,
            level,
            previousLevel,
            null);
        m_sender.SendHealth(player);

        // An important transition (Persistence §6). A character logging out or expelled shares nothing, so this never
        // takes the place of the checkpoint that ends its time in the world.
        if (!character.IsLoggingOut && !character.IsExpelled)
        {
            m_lifetime.QueueCheckpoint(character);
        }
    }

    private ExperienceTableDefinition TableOf(PlayerEntity player)
    {
        return m_content.ExperienceTables[m_content.Jobs[player.Job].ExperienceTable];
    }
}
}
