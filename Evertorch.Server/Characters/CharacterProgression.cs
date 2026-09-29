using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Shares a dead monster's base and job experience among the characters that damaged it and levels them up
///     (Gameplay Systems §2.1), and keeps their quests: acceptance, the kills that count, and the reward once a turn-in
///     has committed
///     (Gameplay Systems §2.2). Tick thread only.
/// </summary>
public sealed class CharacterProgression
{
    private const string QuestAccepted = "accepted";
    private const string QuestCompleted = "completed";

    // Seven fields, one more than LoggerMessage.Define takes; a turn-in is rare enough for the formatted overload.
    private const string QuestCompletedMessage =
        "Character {Character} on connection {Connection} completed {Quest} for {Experience} base experience, "
        + "{JobExperience} job experience, and {Coins} coins (operation {OperationId}).";

    private static readonly Action<ILogger, long, long, int, int, Exception?> LogLeveledUp =
        LoggerMessage.Define<long, long, int, int>(
            LogLevel.Information,
            new EventId(1006, "CharacterLeveledUp"),
            "Character {Character} on connection {Connection} reached level {Level} from level {PreviousLevel}.");

    private static readonly Action<ILogger, long, long, string, Exception?> LogQuestAccepted =
        LoggerMessage.Define<long, long, string>(
            LogLevel.Information,
            new EventId(1009, "QuestAccepted"),
            "Character {Character} on connection {Connection} accepted {Quest}.");

    private static readonly EventId QuestCompletedEvent = new(1010, "QuestCompleted");

    private static readonly Action<ILogger, long, long, int, int, Exception?> LogJobLeveledUp =
        LoggerMessage.Define<long, long, int, int>(
            LogLevel.Information,
            new EventId(1011, "JobLeveledUp"),
            "Character {Character} on connection {Connection} reached job level {JobLevel} from job level "
            + "{PreviousJobLevel}.");

    private readonly SessionRegistry m_sessions;
    private readonly CharacterLifetime m_lifetime;
    private readonly CharacterStats m_stats;
    private readonly IProgressionRules m_rules;
    private readonly ServerContent m_content;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<CharacterProgression> m_logger;
    private readonly float m_npcReach;

    public CharacterProgression(
        SessionRegistry sessions,
        CharacterLifetime lifetime,
        CharacterStats stats,
        IProgressionRules rules,
        ServerContent content,
        MessageSender sender,
        ServerInstruments instruments,
        IOptions<WorldOptions> world,
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
        m_npcReach = NpcInteraction.Range + world.Value.AttackRangeTolerance;
    }

    /// <summary>
    ///     What <paramref name="player" />'s next level needs in all; 0 at its job's level cap.
    /// </summary>
    public long ExperienceToNextLevel(PlayerEntity player)
    {
        return m_rules.ExperienceToNextLevel(TableOf(player), player.Level);
    }

    /// <summary>
    ///     The level and experience <paramref name="player" /> would reach with <paramref name="experience" /> more, as
    ///     an award would leave them.
    /// </summary>
    public LevelProgress WithExperience(PlayerEntity player, long experience)
    {
        return m_rules.AddExperience(TableOf(player), new LevelProgress(player.Level, player.Experience), experience);
    }

    /// <summary>
    ///     The job level and job experience <paramref name="player" /> would reach with <paramref name="experience" />
    ///     more, as an award would leave them.
    /// </summary>
    public LevelProgress WithJobExperience(PlayerEntity player, long experience)
    {
        return m_rules.AddExperience(
            JobTableOf(player),
            new LevelProgress(player.JobLevel, player.JobExperience),
            experience);
    }

    /// <summary>
    ///     Awards <paramref name="monster" />'s base and job experience, which has just died on <paramref name="map" />:
    ///     each character in its damage log that may share them gets its share of the whole log of each (Gameplay
    ///     Systems §2.1).
    /// </summary>
    public void AwardKill(MapInstance map, MonsterEntity monster)
    {
        IReadOnlyList<DamageLogEntry> log = monster.DamageLog;
        long baseExperience = monster.Definition.BaseExperience;
        long jobExperience = monster.Definition.JobExperience;
        if ((baseExperience <= 0 && jobExperience <= 0) || log.Count == 0)
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
            if (TryGetSharer(entry.Character, map, out CharacterSession? character))
            {
                if (baseExperience > 0)
                {
                    Award(character!, m_rules.ShareExperience(baseExperience, entry.Damage, total));
                }

                if (jobExperience > 0)
                {
                    AwardJob(character!, m_rules.ShareExperience(jobExperience, entry.Damage, total));
                }
            }
        }
    }

    /// <summary>
    ///     Counts <paramref name="monster" />'s death, which has just happened on <paramref name="map" />, for every
    ///     character in its damage log that may share its experience, whether or not it gives any (Gameplay Systems
    ///     §2.2): each of its active quests after this monster gains one, up to the count. Reaching the count queues a
    ///     checkpoint, as a level-up does.
    /// </summary>
    public void CreditQuests(MapInstance map, MonsterEntity monster)
    {
        foreach (DamageLogEntry entry in monster.DamageLog)
        {
            if (!TryGetSharer(entry.Character, map, out CharacterSession? character))
            {
                continue;
            }

            bool isAdvanced = false;
            bool isReady = false;
            foreach (CharacterQuest quest in character!.Quests.Entries)
            {
                QuestDefinition definition = m_content.Quests[quest.Quest];
                if (quest.IsCompleted
                    || definition.Monster != monster.Definition.Id
                    || quest.Progress >= definition.Count)
                {
                    continue;
                }

                quest.Progress++;
                isAdvanced = true;
                isReady |= quest.Progress == definition.Count;
            }

            if (isAdvanced && character.Connection != null)
            {
                character.Connection.NeedsQuestLog = true;
            }

            if (isReady)
            {
                m_lifetime.QueueCheckpoint(character);
            }
        }
    }

    /// <summary>
    ///     Checks an acceptance (Gameplay Systems §2.2, after the checks of §6.1) and, when it passes, adds the quest
    ///     active with no progress and queues a checkpoint. The caller has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryAcceptQuest(ClientSession session, EntityId npc, QuestDefinitionId quest)
    {
        CharacterSession character = session.Character!;
        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? giver);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!m_content.Quests.TryGetValue(quest, out QuestDefinition? definition)
            || definition!.Giver != giver!.Definition.Id)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // Taken once per character: active or completed, it cannot be accepted again.
        if (character.Quests.TryGet(quest, out CharacterQuest? _))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        character.Quests.Accept(quest);
        m_lifetime.QueueCheckpoint(character);
        session.NeedsQuestLog = true;
        LogQuestAccepted(m_logger, character.Character.Value, session.Connection.Value, quest.Value, null);
        m_instruments.RecordQuest(QuestAccepted);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Completes <paramref name="quest" /> once its turn-in has committed and gives the reward's experience through
    ///     the path a kill's share takes, the surplus carrying through several levels (Gameplay Systems §2.2).
    /// </summary>
    public void CompleteQuest(CharacterSession character, QuestDefinitionId quest, Guid operationId)
    {
        QuestDefinition definition = m_content.Quests[quest];
        if (character.Quests.TryGet(quest, out CharacterQuest? entry))
        {
            entry!.IsCompleted = true;
            entry.Progress = definition.Count;
        }

        if (definition.BaseExperience > 0)
        {
            Award(character, definition.BaseExperience);
        }

        if (definition.JobExperience > 0)
        {
            AwardJob(character, definition.JobExperience);
        }

        if (character.Connection != null)
        {
            character.Connection.NeedsQuestLog = true;
        }

        m_logger.LogInformation(
            QuestCompletedEvent,
            QuestCompletedMessage,
            character.Character.Value,
            character.Connection?.Connection.Value ?? 0,
            quest.Value,
            definition.BaseExperience,
            definition.JobExperience,
            definition.Currency,
            operationId);
        m_instruments.RecordQuest(QuestCompleted);
    }

    // Alive on the monster's map, not logging out, and not expelled; a character in its reconnect grace period is
    // still on the map and shares.
    private bool TryGetSharer(CharacterId id, MapInstance map, out CharacterSession? character)
    {
        return m_sessions.TryGetCharacter(id, out character)
            && character != null
            && character.Map == map
            && !character.Player.IsDead
            && !character.IsLoggingOut
            && !character.IsExpelled;
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

    // Job experience carries through job levels as base experience does, and is lost at the job's cap. A job level
    // grants a skill point and restores nothing; the sheet the owner sees follows (Gameplay Systems §2.1).
    private void AwardJob(CharacterSession character, long experience)
    {
        PlayerEntity player = character.Player;
        int previousLevel = player.JobLevel;
        LevelProgress progress = WithJobExperience(player, experience);
        player.JobLevel = progress.Level;
        player.JobExperience = progress.Experience;
        m_instruments.RecordJobExperience(experience);
        if (progress.Level == previousLevel)
        {
            return;
        }

        m_instruments.RecordJobLevelUps(progress.Level - previousLevel);
        LogJobLeveledUp(
            m_logger,
            character.Character.Value,
            character.Connection?.Connection.Value ?? 0,
            progress.Level,
            previousLevel,
            null);
        if (!character.IsLoggingOut && !character.IsExpelled)
        {
            m_lifetime.QueueCheckpoint(character);
        }
    }

    private void LevelUp(CharacterSession character, int level, int previousLevel)
    {
        PlayerEntity player = character.Player;

        // A level-up restores HP and SP in full, as the reference does, but never revives: a quest's reward can reach
        // a character that died while its turn-in was in flight.
        player.Level = level;
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (!player.IsDead)
        {
            player.CurrentHealth = player.MaxHealth;
            player.CurrentSpirit = player.MaxSpirit;
        }

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

    private ExperienceTableDefinition JobTableOf(PlayerEntity player)
    {
        return m_content.ExperienceTables[m_content.Jobs[player.Job].JobExperienceTable];
    }
}
}
