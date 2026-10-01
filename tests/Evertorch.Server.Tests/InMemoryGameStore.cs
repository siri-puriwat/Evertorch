using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A test double of the durable store for simulation tests that need persistence behaviour but not a database
///     (Coding Standards §10). <see cref="PendingMigrations" /> and <see cref="IsUnavailable" /> script its answers.
/// </summary>
internal sealed class InMemoryGameStore : IGameStore
{
    // The coin cap, as the PostgreSQL schema holds it.
    private const long MaxCoins = 1_000_000_000;

    private readonly object m_gate = new();
    private readonly Dictionary<string, long> m_accounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_disabledLogins = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Password> m_passwords = new();
    private readonly Dictionary<string, TokenEntry> m_tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Row> m_characters = new();
    private readonly Dictionary<Guid, LedgerEntry> m_ledger = new();
    private readonly Dictionary<long, PartyEntry> m_parties = new();
    private long m_lastParty;
    private long m_lastAccount;
    private long m_lastCharacter;
    private long m_lastItem;

    public IReadOnlyList<string> PendingMigrations { get; set; } = Array.Empty<string>();

    public bool IsUnavailable { get; set; }

    /// <summary>
    ///     When set, asking for pending migrations throws this, the way a database that answers with an error of its
    ///     own (a refused password, say) fails rather than being unreachable.
    /// </summary>
    public Exception? MigrationQueryFailure { get; set; }

    /// <summary>
    ///     When set, runs at the start of each pending-migrations query, before the store answers, so a test can hold a
    ///     probe while it queues work.
    /// </summary>
    public Action? BeforeMigrationQuery { get; set; }

    /// <summary>
    ///     When set, runs at the start of each account creation and password change, before the store answers, so a
    ///     test can hold the console's command while it types another.
    /// </summary>
    public Action? BeforeAccountWrite { get; set; }

    /// <summary>
    ///     When set, the next character created gets this ID instead of the next free one, so a test can name it.
    /// </summary>
    public long? NextCharacterId { get; set; }

    /// <summary>
    ///     The build every character created from now on is stored with; null stores a new character as the server made
    ///     it.
    /// </summary>
    public BuildSeed? SeedOnCreate { get; set; }

    /// <summary>
    ///     Every job change asked of the store, in order, including repeats.
    /// </summary>
    public List<JobChangeCommit> JobChangeCommits { get; } = new();

    /// <summary>
    ///     How many job changes still to commit throw as if their answer was lost after the commit.
    /// </summary>
    public int AmbiguousJobChangeFailures { get; set; }

    /// <summary>
    ///     Every party change asked of the store, in order, including repeats.
    /// </summary>
    public List<PartyChange> PartyChanges { get; } = new();

    /// <summary>
    ///     How many party changes still to commit throw as if their answer was lost after the commit.
    /// </summary>
    public int AmbiguousPartyFailures { get; set; }

    /// <summary>
    ///     Every login provisioned, in order, including repeats.
    /// </summary>
    public List<string> Logins { get; } = new();

    /// <summary>
    ///     Every checkpoint written, in order.
    /// </summary>
    public List<CharacterCheckpoint> Checkpoints { get; } = new();

    /// <summary>
    ///     How many times each character was loaded.
    /// </summary>
    public Dictionary<long, int> Loads { get; } = new();

    /// <summary>
    ///     How many of the next pickup commits succeed and then throw as if the connection dropped before the answer:
    ///     the ambiguous failure of Persistence §5.
    /// </summary>
    public int AmbiguousPickupFailures { get; set; }

    /// <summary>
    ///     Every pickup commit attempted, in order, including ones that changed nothing.
    /// </summary>
    public List<PickupCommit> PickupCommits { get; } = new();

    /// <summary>
    ///     How many of the next equip or unequip commits succeed and then throw as if the answer was lost.
    /// </summary>
    public int AmbiguousEquipmentFailures { get; set; }

    /// <summary>
    ///     Every equip and unequip commit attempted, in order, by operation ID.
    /// </summary>
    public List<Guid> EquipmentCommits { get; } = new();

    /// <summary>
    ///     How many of the next consume commits succeed and then throw as if the answer was lost.
    /// </summary>
    public int AmbiguousConsumeFailures { get; set; }

    /// <summary>
    ///     Every consume commit attempted, in order, by operation ID.
    /// </summary>
    public List<Guid> ConsumeCommits { get; } = new();

    /// <summary>
    ///     How many of the next buy or sell commits succeed and then throw as if the answer was lost.
    /// </summary>
    public int AmbiguousTradeFailures { get; set; }

    /// <summary>
    ///     Every buy and sell commit attempted, in order, by operation ID.
    /// </summary>
    public List<Guid> TradeCommits { get; } = new();

    /// <summary>
    ///     How many of the next turn-in commits succeed and then throw as if the answer was lost.
    /// </summary>
    public int AmbiguousRewardFailures { get; set; }

    /// <summary>
    ///     Every turn-in commit attempted, in order.
    /// </summary>
    public List<QuestRewardCommit> RewardCommits { get; } = new();

    /// <summary>
    ///     How many of the next operation lookups fail with an error of the store's own, not an outage.
    /// </summary>
    public int FailingLookups { get; set; }

    /// <summary>
    ///     Every operation lookup asked for, in order, by operation ID, including the ones that failed or met an outage.
    /// </summary>
    public List<Guid> Lookups { get; } = new();

    public int LedgerCount
    {
        get
        {
            lock (m_gate)
            {
                return m_ledger.Count;
            }
        }
    }

    public int AccountCount
    {
        get
        {
            lock (m_gate)
            {
                return m_accounts.Count;
            }
        }
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        BeforeMigrationQuery?.Invoke();
        ThrowIfUnavailable();
        if (MigrationQueryFailure != null)
        {
            throw MigrationQueryFailure;
        }

        return Task.FromResult(PendingMigrations);
    }

    public Task<AccountId?> ProvisionAccountAsync(
        string loginNormalized,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            Logins.Add(loginNormalized);
            if (!m_accounts.TryGetValue(loginNormalized, out long id))
            {
                id = ++m_lastAccount;
                m_accounts.Add(loginNormalized, id);
            }

            AccountId? account = m_disabledLogins.Contains(loginNormalized) ? null : new AccountId(id);
            return Task.FromResult(account);
        }
    }

    public Task<AccountId?> CreateAccountAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        BeforeAccountWrite?.Invoke();
        ThrowIfUnavailable();
        lock (m_gate)
        {
            AccountId? account = null;
            if (!m_accounts.ContainsKey(loginNormalized))
            {
                long id = ++m_lastAccount;
                m_accounts.Add(loginNormalized, id);
                m_passwords.Add(id, new Password(passwordScheme, passwordHash));
                account = new AccountId(id);
            }

            return Task.FromResult(account);
        }
    }

    public Task<AccountId?> SetAccountPasswordAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        BeforeAccountWrite?.Invoke();
        ThrowIfUnavailable();
        lock (m_gate)
        {
            AccountId? account = null;
            if (m_accounts.TryGetValue(loginNormalized, out long id))
            {
                m_passwords[id] = new Password(passwordScheme, passwordHash);
                foreach (string key in m_tokens.Where(pair => pair.Value.Account == id).Select(pair => pair.Key)
                             .ToList())
                {
                    m_tokens.Remove(key);
                }

                account = new AccountId(id);
            }

            return Task.FromResult(account);
        }
    }

    public Task<AccountCredentials?> FindAccountCredentialsAsync(
        string loginNormalized,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            AccountCredentials? credentials = null;
            if (m_accounts.TryGetValue(loginNormalized, out long id))
            {
                m_passwords.TryGetValue(id, out Password? password);
                credentials = new AccountCredentials(
                    new AccountId(id),
                    password?.Scheme,
                    password?.Hash,
                    m_disabledLogins.Contains(loginNormalized));
            }

            return Task.FromResult(credentials);
        }
    }

    public Task IssueSessionTokenAsync(
        AccountId account,
        byte[] tokenHash,
        DateTime issuedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            m_tokens.Add(Convert.ToHexString(tokenHash), new TokenEntry(account.Value, issuedAt, expiresAt));
            DateTime forgotten = issuedAt - SessionTokenLimits.ExpiredRetention;
            var removed = m_tokens
                .Where(pair => pair.Value.ExpiresAt < forgotten)
                .Select(pair => pair.Key)
                .Concat(
                    m_tokens
                        .Where(pair => pair.Value.Account == account.Value && pair.Value.ExpiresAt > issuedAt)
                        .OrderByDescending(pair => pair.Value.IssuedAt)
                        .ThenByDescending(pair => pair.Key, StringComparer.Ordinal)
                        .Skip(SessionTokenLimits.MaxLiveTokensPerAccount)
                        .Select(pair => pair.Key))
                .ToList();
            foreach (string key in removed)
            {
                m_tokens.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    public Task<StoredSessionToken?> FindSessionTokenAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            StoredSessionToken? token = null;
            if (m_tokens.TryGetValue(Convert.ToHexString(tokenHash), out TokenEntry? entry))
            {
                string login = m_accounts.Single(pair => pair.Value == entry.Account).Key;
                token = new StoredSessionToken(
                    new AccountId(entry.Account),
                    entry.ExpiresAt,
                    m_disabledLogins.Contains(login));
            }

            return Task.FromResult(token);
        }
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(
        AccountId account,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            return Task.FromResult(List(account));
        }
    }

    public Task<CharacterCreation> CreateCharacterAsync(
        AccountId account,
        NewCharacter character,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            if (List(account).Count >= maxCharacters)
            {
                return Task.FromResult(new CharacterCreation(CharacterCreationStatus.LimitReached, 0, List(account)));
            }

            if (m_characters.Values.Any(row => string.Equals(
                    row.Name,
                    character.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new CharacterCreation(CharacterCreationStatus.NameTaken, 0, List(account)));
            }

            long id = NextCharacterId ?? m_lastCharacter + 1;
            NextCharacterId = null;
            m_lastCharacter = Math.Max(m_lastCharacter, id);
            var row = new Row(account, character);
            if (SeedOnCreate != null)
            {
                row.Job = SeedOnCreate.Job;
                row.JobLevel = SeedOnCreate.JobLevel;
                foreach (KeyValuePair<string, int> skill in SeedOnCreate.Skills)
                {
                    row.Skills[skill.Key] = skill.Value;
                }
            }

            m_characters.Add(id, row);
            return Task.FromResult(new CharacterCreation(CharacterCreationStatus.Created, id, List(account)));
        }
    }

    public Task<StoredCharacter?> LoadCharacterAsync(
        AccountId account,
        long characterId,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            Loads[characterId] = Loads.TryGetValue(characterId, out int count) ? count + 1 : 1;
            StoredCharacter? stored = m_characters.TryGetValue(characterId, out Row? row) && row.Account == account
                ? row.ToStored(characterId)
                : null;
            return Task.FromResult(stored);
        }
    }

    public Task<StoredParty?> LoadPartyAsync(long characterId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            StoredParty? stored = null;
            foreach (KeyValuePair<long, PartyEntry> party in m_parties)
            {
                if (party.Value.Members.Any(member => member.Character == characterId))
                {
                    stored = ToStored(party.Key, party.Value);
                }
            }

            return Task.FromResult(stored);
        }
    }

    public Task SaveCheckpointAsync(CharacterCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            Checkpoints.Add(checkpoint);
            if (m_characters.TryGetValue(checkpoint.CharacterId, out Row? row))
            {
                row.Map = checkpoint.Map.Value;
                row.Position = checkpoint.Position;
                row.Health = checkpoint.Health;
                row.Spirit = checkpoint.Spirit;
                bool isNotLower = checkpoint.Level > row.Level
                    || (checkpoint.Level == row.Level && checkpoint.Experience >= row.Experience);
                if (isNotLower && !checkpoint.IsCommitInFlight)
                {
                    row.Level = checkpoint.Level;
                    row.Experience = checkpoint.Experience;
                }

                bool isJobNotLower = checkpoint.JobLevel > row.JobLevel
                    || (checkpoint.JobLevel == row.JobLevel && checkpoint.JobExperience >= row.JobExperience);
                if (isJobNotLower && !checkpoint.IsCommitInFlight && checkpoint.Job == row.Job)
                {
                    row.JobLevel = checkpoint.JobLevel;
                    row.JobExperience = checkpoint.JobExperience;
                }

                if (checkpoint.Stats.HasValue && !checkpoint.IsCommitInFlight)
                {
                    row.Stats = checkpoint.Stats.Value;
                }

                if (checkpoint.Skills != null && !checkpoint.IsCommitInFlight)
                {
                    row.Skills.Clear();
                    foreach (StoredSkill skill in checkpoint.Skills)
                    {
                        row.Skills[skill.SkillDefinitionId] = skill.Level;
                    }
                }

                foreach (StoredQuest quest in checkpoint.Quests)
                {
                    if (!row.Quests.TryGetValue(quest.QuestDefinitionId, out StoredQuest? stored))
                    {
                        row.Quests.Add(
                            quest.QuestDefinitionId,
                            new StoredQuest(quest.QuestDefinitionId, false, quest.Progress));
                    }
                    else if (!stored.IsCompleted && stored.Progress < quest.Progress)
                    {
                        row.Quests[quest.QuestDefinitionId] =
                            new StoredQuest(quest.QuestDefinitionId, false, quest.Progress);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<InventoryResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            PickupCommits.Add(pickup);
            InventoryResult result = Find(pickup.DropId, pickup.CharacterId, Array.Empty<long>()) ?? Commit(pickup);
            if (AmbiguousPickupFailures > 0 && result.Status == InventoryStatus.Committed)
            {
                AmbiguousPickupFailures--;
                throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
            }

            return Task.FromResult(result);
        }
    }

    public Task<InventoryResult> CommitEquipAsync(EquipCommit equip, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            EquipmentCommits.Add(equip.OperationId);
            InventoryResult result = Find(equip.OperationId, equip.CharacterId, equip.ReportRows) ?? Equip(equip);
            return AnswerEquipment(result);
        }
    }

    public Task<InventoryResult> CommitUnequipAsync(UnequipCommit unequip, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            EquipmentCommits.Add(unequip.OperationId);
            InventoryResult result = Find(unequip.OperationId, unequip.CharacterId, Array.Empty<long>())
                ?? Unequip(unequip);
            return AnswerEquipment(result);
        }
    }

    public Task<InventoryResult> CommitConsumeAsync(ConsumeCommit consume, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            ConsumeCommits.Add(consume.OperationId);
            InventoryResult result = Find(consume.OperationId, consume.CharacterId, Array.Empty<long>())
                ?? Consume(consume);
            if (AmbiguousConsumeFailures > 0 && result.Status == InventoryStatus.Committed)
            {
                AmbiguousConsumeFailures--;
                throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
            }

            return Task.FromResult(result);
        }
    }

    public Task<InventoryResult> CommitBuyAsync(BuyCommit buy, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            TradeCommits.Add(buy.OperationId);
            return AnswerTrade(Find(buy.OperationId, buy.CharacterId, Array.Empty<long>()) ?? Buy(buy));
        }
    }

    public Task<InventoryResult> CommitSellAsync(SellCommit sell, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            TradeCommits.Add(sell.OperationId);
            return AnswerTrade(Find(sell.OperationId, sell.CharacterId, Array.Empty<long>()) ?? Sell(sell));
        }
    }

    public Task<InventoryResult> CommitQuestRewardAsync(QuestRewardCommit reward, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            RewardCommits.Add(reward);
            InventoryResult result =
                Find(reward.OperationId, reward.CharacterId, Array.Empty<long>()) ?? Reward(reward);
            if (AmbiguousRewardFailures > 0 && result.Status == InventoryStatus.Committed)
            {
                AmbiguousRewardFailures--;
                throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
            }

            return Task.FromResult(result);
        }
    }

    public Task<InventoryResult> CommitJobChangeAsync(JobChangeCommit change, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            JobChangeCommits.Add(change);
            Row row = m_characters[change.CharacterId];
            InventoryResult result = row.Job == change.ToJob
                ? Changed(change.OperationId, change.CharacterId, row)
                : row.Job != change.FromJob
                    ? Unchanged(InventoryStatus.Refused, row)
                    : ChangeJob(change, row);
            if (AmbiguousJobChangeFailures > 0 && result.Status == InventoryStatus.Committed)
            {
                AmbiguousJobChangeFailures--;
                throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
            }

            return Task.FromResult(result);
        }
    }

    public Task<InventoryResult?> FindJobChangeAsync(
        Guid operationId,
        long characterId,
        string job,
        CancellationToken cancellationToken)
    {
        lock (m_gate)
        {
            Lookups.Add(operationId);
        }

        ThrowIfUnavailable();
        lock (m_gate)
        {
            Row row = m_characters[characterId];
            return Task.FromResult(row.Job == job ? Changed(operationId, characterId, row) : null);
        }
    }

    public Task<InventoryResult?> FindOperationAsync(
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken)
    {
        lock (m_gate)
        {
            Lookups.Add(operationId);
        }

        ThrowIfUnavailable();
        lock (m_gate)
        {
            if (FailingLookups > 0)
            {
                FailingLookups--;
                throw new InvalidOperationException("scripted failure of the lookup");
            }

            return Task.FromResult(Find(operationId, characterId, rowIds));
        }
    }

    public Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            IReadOnlyList<string> ids = m_characters.Values
                .SelectMany(row => new[] { row.Job, row.Map }
                    .Concat(row.Items.Select(item => item.ItemDefinitionId))
                    .Concat(row.Quests.Keys)
                    .Concat(row.Skills.Keys))
                .Distinct()
                .ToList();
            return Task.FromResult(ids);
        }
    }

    public Task<PartyChangeResult> CommitPartyChangeAsync(PartyChange change, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            PartyChanges.Add(change);
            PartyChangeResult result = change.Kind == PartyChangeKind.Create ? Create(change) : Change(change);
            if (AmbiguousPartyFailures > 0 && result.Status == PartyChangeStatus.Committed)
            {
                AmbiguousPartyFailures--;
                throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
            }

            return Task.FromResult(result);
        }
    }

    public Task<StoredParty?> FindPartyStateAsync(long characterId, CancellationToken cancellationToken)
    {
        return LoadPartyAsync(characterId, cancellationToken);
    }

    // The same rules as PostgresGameStore's (Persistence §5).
    private PartyChangeResult Create(PartyChange change)
    {
        long? leaderParty = PartyOf(change.Actor);
        long? inviteeParty = PartyOf(change.Target);
        if (leaderParty != null && leaderParty == inviteeParty)
        {
            return Result(
                m_parties[leaderParty.Value].Leader == change.Actor
                    ? PartyChangeStatus.Committed
                    : PartyChangeStatus.AlreadyInParty,
                leaderParty);
        }

        if (leaderParty != null || inviteeParty != null)
        {
            return Result(PartyChangeStatus.AlreadyInParty, PartyOf(change.Actor));
        }

        if (m_characters[change.Actor].Account == m_characters[change.Target].Account)
        {
            return Result(PartyChangeStatus.SameAccount, null);
        }

        var party = new PartyEntry { Leader = change.Actor };
        party.Members.Add((change.Actor, 1));
        party.Members.Add((change.Target, 2));
        m_parties.Add(++m_lastParty, party);
        return Result(PartyChangeStatus.Committed, m_lastParty);
    }

    private PartyChangeResult Change(PartyChange change)
    {
        if (!m_parties.TryGetValue(change.PartyId, out PartyEntry? party))
        {
            bool isGone = change.Kind == PartyChangeKind.Leave || change.Kind == PartyChangeKind.Kick;
            return Result(isGone ? PartyChangeStatus.Committed : PartyChangeStatus.NoSuchParty, null);
        }

        PartyChangeStatus status = change.Kind switch
        {
            PartyChangeKind.Join => Join(party, change),
            PartyChangeKind.Leave => Remove(change.PartyId, party, change.Actor),
            PartyChangeKind.Kick => party.Leader != change.Actor ? PartyChangeStatus.NotTheLeader
                : change.Target == change.Actor ? PartyChangeStatus.NotAMember
                : Remove(change.PartyId, party, change.Target),
            _ => Lead(party, change)
        };
        return Result(status, m_parties.ContainsKey(change.PartyId) ? change.PartyId : null);
    }

    private PartyChangeStatus Join(PartyEntry party, PartyChange change)
    {
        if (party.Members.Any(member => member.Character == change.Target))
        {
            return PartyChangeStatus.Committed;
        }

        if (party.Leader != change.Actor)
        {
            return PartyChangeStatus.NotTheLeader;
        }

        if (PartyOf(change.Target) != null)
        {
            return PartyChangeStatus.AlreadyInParty;
        }

        if (party.Members.Count >= change.MaxMembers)
        {
            return PartyChangeStatus.PartyFull;
        }

        AccountId invitee = m_characters[change.Target].Account;
        if (party.Members.Any(member => m_characters[member.Character].Account == invitee))
        {
            return PartyChangeStatus.SameAccount;
        }

        party.Members.Add((change.Target, party.Members.Max(member => member.JoinOrder) + 1));
        return PartyChangeStatus.Committed;
    }

    private PartyChangeStatus Remove(long id, PartyEntry party, long character)
    {
        int index = party.Members.FindIndex(member => member.Character == character);
        if (index < 0)
        {
            return PartyChangeStatus.Committed;
        }

        party.Members.RemoveAt(index);
        if (party.Members.Count < 2)
        {
            m_parties.Remove(id);
        }
        else if (party.Leader == character)
        {
            party.Leader = party.Members.OrderBy(member => member.JoinOrder).First().Character;
        }

        return PartyChangeStatus.Committed;
    }

    private static PartyChangeStatus Lead(PartyEntry party, PartyChange change)
    {
        if (party.Leader == change.Target)
        {
            return PartyChangeStatus.Committed;
        }

        if (party.Leader != change.Actor)
        {
            return PartyChangeStatus.NotTheLeader;
        }

        if (party.Members.All(member => member.Character != change.Target))
        {
            return PartyChangeStatus.NotAMember;
        }

        party.Leader = change.Target;
        return PartyChangeStatus.Committed;
    }

    private long? PartyOf(long character)
    {
        foreach (KeyValuePair<long, PartyEntry> party in m_parties)
        {
            if (party.Value.Members.Any(member => member.Character == character))
            {
                return party.Key;
            }
        }

        return null;
    }

    private PartyChangeResult Result(PartyChangeStatus status, long? party)
    {
        return new PartyChangeResult(status, party == null ? null : ToStored(party.Value, m_parties[party.Value]));
    }

    private StoredParty ToStored(long id, PartyEntry party)
    {
        return new StoredParty(
            id,
            party.Leader,
            party.Members
                .OrderBy(member => member.JoinOrder)
                .Select(member =>
                {
                    Row row = m_characters[member.Character];
                    return new StoredPartyMember(
                        member.Character,
                        row.Name,
                        row.Job,
                        row.Level,
                        row.Account,
                        member.JoinOrder);
                })
                .ToList());
    }

    /// <summary>
    ///     How many session tokens are stored for account <paramref name="account" />.
    /// </summary>
    public int SessionTokenCount(AccountId account)
    {
        lock (m_gate)
        {
            return m_tokens.Values.Count(entry => entry.Account == account.Value);
        }
    }

    /// <summary>
    ///     The starting values the server chose for character <paramref name="id" />.
    /// </summary>
    public NewCharacter CreatedCharacter(long id)
    {
        lock (m_gate)
        {
            return m_characters[id].Created;
        }
    }

    /// <summary>
    ///     The stored state of character <paramref name="id" />, as a load would return it.
    /// </summary>
    public StoredCharacter Stored(long id)
    {
        lock (m_gate)
        {
            return m_characters[id].ToStored(id);
        }
    }

    /// <summary>
    ///     Changes what is stored for character <paramref name="id" />, as a content change or an earlier session
    ///     would have left it.
    /// </summary>
    public void Edit(
        long id,
        string? job = null,
        string? map = null,
        WorldPosition? position = null,
        int? health = null,
        string? item = null,
        int? spirit = null,
        int? level = null,
        long? experience = null,
        long? coins = null,
        int? jobLevel = null,
        long? jobExperience = null,
        PrimaryStats? stats = null,
        IReadOnlyDictionary<string, int>? skills = null)
    {
        lock (m_gate)
        {
            Row row = m_characters[id];
            row.Job = job ?? row.Job;
            row.Map = map ?? row.Map;
            row.Position = position ?? row.Position;
            row.Health = health ?? row.Health;
            row.Spirit = spirit ?? row.Spirit;
            row.Level = level ?? row.Level;
            row.Experience = experience ?? row.Experience;
            row.Coins = coins ?? row.Coins;
            row.JobLevel = jobLevel ?? row.JobLevel;
            row.JobExperience = jobExperience ?? row.JobExperience;
            row.Stats = stats ?? row.Stats;
            if (skills != null)
            {
                row.Skills.Clear();
                foreach (KeyValuePair<string, int> skill in skills)
                {
                    row.Skills[skill.Key] = skill.Value;
                }
            }

            if (item != null)
            {
                row.Items.Add(new StoredItem(++m_lastItem, item, 1));
            }
        }
    }

    /// <summary>
    ///     Adds <paramref name="rows" /> stacks of <paramref name="item" /> to character <paramref name="id" /> and sets
    ///     its inventory revision, as earlier pickups would have left them.
    /// </summary>
    public void GiveItems(long id, string item, int rows, int quantity, uint revision)
    {
        lock (m_gate)
        {
            Row row = m_characters[id];
            for (int index = 0; index < rows; index++)
            {
                row.Items.Add(new StoredItem(++m_lastItem, item, quantity));
            }

            row.InventoryRevision = revision;
        }
    }

    /// <summary>
    ///     Wears row <paramref name="item" /> of character <paramref name="id" /> in <paramref name="slot" />, as an
    ///     earlier equip would have left it.
    /// </summary>
    public void Wear(long id, long item, string slot)
    {
        lock (m_gate)
        {
            m_characters[id].Equipment[slot] = item;
        }
    }

    /// <summary>
    ///     Stores <paramref name="quest" /> for character <paramref name="id" /> with <paramref name="progress" />, as an
    ///     earlier checkpoint or turn-in would have left it.
    /// </summary>
    public void GiveQuest(long id, string quest, int progress, bool isCompleted = false)
    {
        lock (m_gate)
        {
            m_characters[id].Quests[quest] = new StoredQuest(quest, isCompleted, progress);
        }
    }

    public void Disable(string loginNormalized)
    {
        lock (m_gate)
        {
            m_disabledLogins.Add(loginNormalized);
        }
    }

    // As the PostgreSQL store answers: the ledger's row and the named ones as they are now, an emptied one at 0.
    private InventoryResult? Find(Guid operationId, long characterId, IReadOnlyCollection<long> rowIds)
    {
        if (!m_ledger.TryGetValue(operationId, out LedgerEntry? entry))
        {
            return null;
        }

        if (entry.Character != characterId)
        {
            return new InventoryResult(InventoryStatus.TakenByOther, 0, 0, Array.Empty<StoredItem>());
        }

        Row row = m_characters[characterId];

        // A turn-in's ledger row names no inventory row.
        IEnumerable<long> named =
            entry.Item == 0 ? rowIds : new[] { entry.Item }.Concat(rowIds.Where(id => id != entry.Item));
        return new InventoryResult(
            InventoryStatus.Committed,
            row.InventoryRevision,
            row.Coins,
            named.Select(id => row.Read(id, id == entry.Item ? entry.ItemDefinition : string.Empty)).ToList());
    }

    // As the PostgreSQL store checks it: the quest not completed, the carried progress, and the coin cap; the row is
    // added when a failed checkpoint left it missing, and the level and experience never go down.
    private InventoryResult Reward(QuestRewardCommit reward)
    {
        Row row = m_characters[reward.CharacterId];
        bool isCompleted = row.Quests.TryGetValue(reward.QuestDefinitionId, out StoredQuest? quest)
            && quest.IsCompleted;
        if (isCompleted || reward.Progress < reward.Count || row.Coins > MaxCoins - reward.Coins)
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        row.Quests[reward.QuestDefinitionId] = new StoredQuest(
            reward.QuestDefinitionId,
            true,
            Math.Max(quest?.Progress ?? 0, reward.Count));
        row.Coins += reward.Coins;
        if (reward.Level > row.Level || (reward.Level == row.Level && reward.Experience >= row.Experience))
        {
            row.Level = reward.Level;
            row.Experience = reward.Experience;
        }

        if (reward.Job == row.Job
            && (reward.JobLevel > row.JobLevel
                || (reward.JobLevel == row.JobLevel && reward.JobExperience >= row.JobExperience)))
        {
            row.JobLevel = reward.JobLevel;
            row.JobExperience = reward.JobExperience;
        }

        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(reward.OperationId, new LedgerEntry(reward.CharacterId, 0));
        return Unchanged(InventoryStatus.Committed, row);
    }

    private InventoryResult Consume(ConsumeCommit consume)
    {
        Row row = m_characters[consume.CharacterId];
        int index = row.Items.FindIndex(item => item.Id == consume.InventoryItemId);
        if (index < 0)
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        StoredItem used = row.Items[index];
        if (used.Quantity == 1)
        {
            row.Items.RemoveAt(index);
        }
        else
        {
            row.Items[index] = new StoredItem(used.Id, used.ItemDefinitionId, used.Quantity - 1);
        }

        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(consume.OperationId, new LedgerEntry(consume.CharacterId, used.Id, used.ItemDefinitionId));
        return new InventoryResult(
            InventoryStatus.Committed,
            row.InventoryRevision,
            row.Coins,
            new[] { row.Read(used.Id, used.ItemDefinitionId) });
    }

    private Task<InventoryResult> AnswerTrade(InventoryResult result)
    {
        if (AmbiguousTradeFailures > 0 && result.Status == InventoryStatus.Committed)
        {
            AmbiguousTradeFailures--;
            throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
        }

        return Task.FromResult(result);
    }

    // As the PostgreSQL store checks it: the coins, then the stack and the rows.
    private InventoryResult Buy(BuyCommit buy)
    {
        Row row = m_characters[buy.CharacterId];
        if (row.Coins < buy.Total)
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        int index = buy.StackLimit == 1
            ? -1
            : row.Items.FindIndex(item => item.ItemDefinitionId == buy.ItemDefinitionId);
        int held = index >= 0 ? row.Items[index].Quantity : 0;
        if (held > buy.StackLimit - buy.Quantity || (index < 0 && row.Items.Count >= buy.MaxRows))
        {
            return Unchanged(InventoryStatus.InventoryFull, row);
        }

        StoredItem stack = index >= 0
            ? new StoredItem(row.Items[index].Id, buy.ItemDefinitionId, held + buy.Quantity)
            : new StoredItem(++m_lastItem, buy.ItemDefinitionId, buy.Quantity);
        if (index >= 0)
        {
            row.Items[index] = stack;
        }
        else
        {
            row.Items.Add(stack);
        }

        row.Coins -= buy.Total;
        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(buy.OperationId, new LedgerEntry(buy.CharacterId, stack.Id, buy.ItemDefinitionId));
        return new InventoryResult(InventoryStatus.Committed, row.InventoryRevision, row.Coins, new[] { stack });
    }

    private InventoryResult Sell(SellCommit sell)
    {
        Row row = m_characters[sell.CharacterId];
        int index = row.Items.FindIndex(item => item.Id == sell.InventoryItemId);
        if (index < 0
            || row.Equipment.ContainsValue(sell.InventoryItemId)
            || row.Items[index].Quantity < sell.Quantity
            || row.Coins > MaxCoins - sell.Proceeds)
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        StoredItem sold = row.Items[index];
        if (sold.Quantity == sell.Quantity)
        {
            row.Items.RemoveAt(index);
        }
        else
        {
            row.Items[index] = new StoredItem(sold.Id, sold.ItemDefinitionId, sold.Quantity - sell.Quantity);
        }

        row.Coins += sell.Proceeds;
        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(sell.OperationId, new LedgerEntry(sell.CharacterId, sold.Id, sold.ItemDefinitionId));
        return new InventoryResult(
            InventoryStatus.Committed,
            row.InventoryRevision,
            row.Coins,
            new[] { row.Read(sold.Id, sold.ItemDefinitionId) });
    }

    private Task<InventoryResult> AnswerEquipment(InventoryResult result)
    {
        if (AmbiguousEquipmentFailures > 0 && result.Status == InventoryStatus.Committed)
        {
            AmbiguousEquipmentFailures--;
            throw new StoreUnavailableException(new TimeoutException("scripted loss of the commit's answer"));
        }

        return Task.FromResult(result);
    }

    private InventoryResult Equip(EquipCommit equip)
    {
        Row row = m_characters[equip.CharacterId];
        row.Equipment.TryGetValue(equip.Slot, out long worn);
        if (row.Items.All(item => item.Id != equip.InventoryItemId) || worn == equip.InventoryItemId)
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        row.Equipment[equip.Slot] = equip.InventoryItemId;
        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(equip.OperationId, new LedgerEntry(equip.CharacterId, equip.InventoryItemId));
        long[] changed = worn == 0 ? new[] { equip.InventoryItemId } : new[] { equip.InventoryItemId, worn };
        return new InventoryResult(
            InventoryStatus.Committed,
            row.InventoryRevision,
            row.Coins,
            changed.Select(row.Read).ToList());
    }

    // As the PostgreSQL store changes a job: job level 1 and job experience 0, and the worn item out of the named slot
    // under the operation's ledger entry; a change that takes nothing off keeps no entry and no new revision.
    private InventoryResult ChangeJob(JobChangeCommit change, Row row)
    {
        row.Job = change.ToJob;
        row.JobLevel = 1;
        row.JobExperience = 0;
        if (change.UnequipSlot == null || !row.Equipment.TryGetValue(change.UnequipSlot, out long worn))
        {
            return Unchanged(InventoryStatus.Committed, row);
        }

        row.Equipment.Remove(change.UnequipSlot);
        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        StoredItem item = row.Read(worn);
        m_ledger.Add(change.OperationId, new LedgerEntry(change.CharacterId, worn, item.ItemDefinitionId));
        return new InventoryResult(InventoryStatus.Committed, row.InventoryRevision, row.Coins, new[] { item });
    }

    // A change that committed: the ledger's answer when it took an item off, else the character as it is.
    private InventoryResult Changed(Guid operationId, long characterId, Row row)
    {
        return Find(operationId, characterId, Array.Empty<long>()) ?? Unchanged(InventoryStatus.Committed, row);
    }

    private InventoryResult Unequip(UnequipCommit unequip)
    {
        Row row = m_characters[unequip.CharacterId];
        if (!row.Equipment.TryGetValue(unequip.Slot, out long worn))
        {
            return Unchanged(InventoryStatus.Refused, row);
        }

        row.Equipment.Remove(unequip.Slot);
        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(unequip.OperationId, new LedgerEntry(unequip.CharacterId, worn));
        return new InventoryResult(
            InventoryStatus.Committed,
            row.InventoryRevision,
            row.Coins,
            new[] { row.Read(worn) });
    }

    private InventoryResult Commit(PickupCommit pickup)
    {
        Row row = m_characters[pickup.CharacterId];
        int index = row.Items.FindIndex(item => item.ItemDefinitionId == pickup.ItemDefinitionId);
        int held = index >= 0 ? row.Items[index].Quantity : 0;
        if (held > pickup.StackLimit - pickup.Amount || (index < 0 && row.Items.Count >= pickup.MaxRows))
        {
            return Unchanged(InventoryStatus.InventoryFull, row);
        }

        StoredItem stack = index >= 0 && pickup.StackLimit > 1
            ? new StoredItem(row.Items[index].Id, pickup.ItemDefinitionId, held + pickup.Amount)
            : new StoredItem(++m_lastItem, pickup.ItemDefinitionId, pickup.Amount);
        if (index >= 0 && pickup.StackLimit > 1)
        {
            row.Items[index] = stack;
        }
        else
        {
            row.Items.Add(stack);
        }

        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(pickup.DropId, new LedgerEntry(pickup.CharacterId, stack.Id));
        return new InventoryResult(InventoryStatus.Committed, row.InventoryRevision, row.Coins, new[] { stack });
    }

    private static InventoryResult Unchanged(InventoryStatus status, Row row)
    {
        return new InventoryResult(status, row.InventoryRevision, row.Coins, Array.Empty<StoredItem>());
    }

    private IReadOnlyList<CharacterSummary> List(AccountId account)
    {
        return m_characters
            .Where(pair => pair.Value.Account == account)
            .OrderBy(pair => pair.Key)
            .Select(pair => new CharacterSummary(pair.Key, pair.Value.Name, pair.Value.Job, 1))
            .ToList();
    }

    private void ThrowIfUnavailable()
    {
        if (IsUnavailable)
        {
            throw new StoreUnavailableException(new TimeoutException("scripted outage"));
        }
    }

    private sealed class Password
    {
        public Password(string scheme, string hash)
        {
            Scheme = scheme;
            Hash = hash;
        }

        public string Scheme { get; }

        public string Hash { get; }
    }

    private sealed class TokenEntry
    {
        public TokenEntry(long account, DateTime issuedAt, DateTime expiresAt)
        {
            Account = account;
            IssuedAt = issuedAt;
            ExpiresAt = expiresAt;
        }

        public long Account { get; }

        public DateTime IssuedAt { get; }

        public DateTime ExpiresAt { get; }
    }

    private sealed class LedgerEntry
    {
        public LedgerEntry(long character, long item, string itemDefinition = "")
        {
            Character = character;
            Item = item;
            ItemDefinition = itemDefinition;
        }

        public long Character { get; }

        public long Item { get; }

        /// <summary>
        ///     The item the entry names, which an emptied row is reported under, as the PostgreSQL store's ledger does.
        /// </summary>
        public string ItemDefinition { get; }
    }

    // A party as stored: its leader and each member's character with its place in the joining order.
    private sealed class PartyEntry
    {
        public long Leader { get; set; }

        public List<(long Character, int JoinOrder)> Members { get; } = new();
    }

    private sealed class Row
    {
        public Row(AccountId account, NewCharacter created)
        {
            Account = account;
            Created = created;
            Name = created.Name;
            Job = created.Job.Value;
            Map = created.Map.Value;
            Position = created.Position;
            Health = created.Health;
            Spirit = created.Spirit;
            Stats = created.Stats;
        }

        public AccountId Account { get; }

        public NewCharacter Created { get; }

        public string Name { get; }

        public string Job { get; set; }

        public string Map { get; set; }

        public WorldPosition Position { get; set; }

        public int Health { get; set; }

        public int Spirit { get; set; }

        public int Level { get; set; } = 1;

        public long Experience { get; set; }

        public int JobLevel { get; set; } = 1;

        public long JobExperience { get; set; }

        public PrimaryStats Stats { get; set; }

        /// <summary>Each learned skill's level, by skill ID.</summary>
        public Dictionary<string, int> Skills { get; } = new(StringComparer.Ordinal);

        public uint InventoryRevision { get; set; }

        public long Coins { get; set; }

        public List<StoredItem> Items { get; } = new();

        /// <summary>The row each worn slot holds, by the database's slot name.</summary>
        public Dictionary<string, long> Equipment { get; } = new(StringComparer.Ordinal);

        /// <summary>Each quest the character has accepted or completed, by quest ID.</summary>
        public Dictionary<string, StoredQuest> Quests { get; } = new(StringComparer.Ordinal);

        // A row as it is now, with its slot; one that no longer exists at quantity 0.
        public StoredItem Read(long id)
        {
            return Read(id, string.Empty);
        }

        public StoredItem Read(long id, string emptiedItem)
        {
            StoredItem? item = Items.SingleOrDefault(candidate => candidate.Id == id);
            string? slot = Equipment.Where(pair => pair.Value == id).Select(pair => pair.Key).FirstOrDefault();
            return item == null
                ? new StoredItem(id, emptiedItem, 0)
                : new StoredItem(item.Id, item.ItemDefinitionId, item.Quantity, slot);
        }

        public StoredCharacter ToStored(long id)
        {
            return new StoredCharacter(
                id,
                Account,
                Name,
                Job,
                Level,
                Experience,
                Stats,
                Health,
                Spirit,
                Map,
                Position,
                InventoryRevision,
                Coins,
                Items.Select(item => Read(item.Id)).ToList(),
                Quests.Values.OrderBy(quest => quest.QuestDefinitionId, StringComparer.Ordinal).ToList(),
                JobLevel,
                JobExperience,
                Skills.OrderBy(skill => skill.Key, StringComparer.Ordinal)
                    .Select(skill => new StoredSkill(skill.Key, skill.Value))
                    .ToList());
        }
    }
}
}
