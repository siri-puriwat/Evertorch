using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Shares a dead monster's base and job experience among the characters that damaged it, evenly within a party, and
///     levels them up (Gameplay Systems §2.1), and keeps their quests: acceptance, the kills that count, and the reward
///     once a turn-in
///     has committed
///     (Gameplay Systems §2.2). Tick thread only.
/// </summary>
public sealed class CharacterProgression
{
    /// <summary>
    ///     A party's pooled share is split evenly only while its eligible members' base levels span at most this (owner
    ///     decision 5 of the pre-Milestone-12 review).
    /// </summary>
    public const int PartyShareLevelSpan = 5;

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
    private readonly PartyRegistry m_parties;
    private readonly float m_npcReach;
    private readonly List<KillShare> m_shares = new();
    private readonly List<ServerParty> m_sharingParties = new();
    private readonly List<CharacterSession> m_members = new();
    private readonly List<CharacterSession> m_credited = new();
    private readonly HashSet<long> m_creditedIds = new();

    public CharacterProgression(
        SessionRegistry sessions,
        CharacterLifetime lifetime,
        CharacterStats stats,
        IProgressionRules rules,
        ServerContent content,
        MessageSender sender,
        ServerInstruments instruments,
        IOptions<WorldOptions> world,
        PartyRegistry parties,
        ILogger<CharacterProgression> logger)
    {
        m_sessions = sessions;
        m_parties = parties;
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
    ///     each character in its damage log that may share them gets its share of the whole log of each, and the shares
    ///     of a party's members are pooled and split evenly among its members who may share, those that did not hurt the
    ///     monster included, while their base levels span at most <see cref="PartyShareLevelSpan" /> (Gameplay Systems
    ///     §2.1).
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

        m_shares.Clear();
        m_sharingParties.Clear();
        foreach (DamageLogEntry entry in log)
        {
            if (!TryGetSharer(entry.Character, map, out CharacterSession? character))
            {
                continue;
            }

            m_parties.TryGetParty(character!.Character, out ServerParty? party);
            if (party != null && !m_sharingParties.Contains(party))
            {
                m_sharingParties.Add(party);
            }

            m_shares.Add(
                new KillShare(
                    character,
                    party,
                    baseExperience > 0 ? m_rules.ShareExperience(baseExperience, entry.Damage, total) : 0,
                    jobExperience > 0 ? m_rules.ShareExperience(jobExperience, entry.Damage, total) : 0));
        }

        // Every split is decided before anything is awarded, so a level gained from this kill changes no span.
        foreach (ServerParty party in m_sharingParties)
        {
            SplitEvenly(map, party);
        }

        foreach (KillShare share in m_shares)
        {
            if (share.Base > 0)
            {
                Award(share.Character, share.Base);
            }

            if (share.Job > 0)
            {
                AwardJob(share.Character, share.Job);
            }
        }
    }

    // A party whose members who may share span at most the level gap pools its members' shares and splits the pool
    // among them all; otherwise each keeps its own.
    private void SplitEvenly(MapInstance map, ServerParty party)
    {
        CollectMembers(map, party);
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        foreach (CharacterSession member in m_members)
        {
            lowest = Math.Min(lowest, member.Player.Level);
            highest = Math.Max(highest, member.Player.Level);
        }

        if (highest - lowest > PartyShareLevelSpan)
        {
            return;
        }

        long pooledBase = 0;
        long pooledJob = 0;
        for (int index = m_shares.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(m_shares[index].Party, party))
            {
                pooledBase += m_shares[index].Base;
                pooledJob += m_shares[index].Job;
                m_shares.RemoveAt(index);
            }
        }

        long eachBase = m_rules.SharePartyExperience(pooledBase, m_members.Count);
        long eachJob = m_rules.SharePartyExperience(pooledJob, m_members.Count);
        foreach (CharacterSession member in m_members)
        {
            m_shares.Add(new KillShare(member, party, eachBase, eachJob));
        }
    }

    // The members of the party that may share a kill on the map, in the order they joined.
    private void CollectMembers(MapInstance map, ServerParty party)
    {
        m_members.Clear();
        foreach (StoredPartyMember member in party.Members)
        {
            if (TryGetSharer(new CharacterId(member.CharacterId), map, out CharacterSession? character))
            {
                m_members.Add(character!);
            }
        }
    }

    /// <summary>
    ///     Counts <paramref name="monster" />'s death, which has just happened on <paramref name="map" />, for every
    ///     character in its damage log that may share its experience, whether or not it gives any, and for every member
    ///     of such a character's party that may share it, whatever their levels (Gameplay Systems §2.2): each of its
    ///     active quests after this monster gains one, up to the count, once per kill. Reaching the count queues a
    ///     checkpoint, as a level-up does.
    /// </summary>
    public void CreditQuests(MapInstance map, MonsterEntity monster)
    {
        m_credited.Clear();
        m_creditedIds.Clear();
        foreach (DamageLogEntry entry in monster.DamageLog)
        {
            if (!TryGetSharer(entry.Character, map, out CharacterSession? character))
            {
                continue;
            }

            AddCredited(character!);
            if (m_parties.TryGetParty(character!.Character, out ServerParty? party))
            {
                CollectMembers(map, party!);
                foreach (CharacterSession member in m_members)
                {
                    AddCredited(member);
                }
            }
        }

        foreach (CharacterSession character in m_credited)
        {
            Credit(character, monster);
        }
    }

    private void AddCredited(CharacterSession character)
    {
        if (m_creditedIds.Add(character.Character.Value))
        {
            m_credited.Add(character);
        }
    }

    private void Credit(CharacterSession character, MonsterEntity monster)
    {
        bool isAdvanced = false;
        bool isReady = false;
        foreach (CharacterQuest quest in character.Quests.Entries)
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

    private readonly struct KillShare
    {
        public KillShare(CharacterSession character, ServerParty? party, long baseShare, long jobShare)
        {
            Character = character;
            Party = party;
            Base = baseShare;
            Job = jobShare;
        }

        public CharacterSession Character { get; }

        public ServerParty? Party { get; }

        public long Base { get; }

        public long Job { get; }
    }
}
}
