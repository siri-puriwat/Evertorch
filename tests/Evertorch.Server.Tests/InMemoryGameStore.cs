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
    private readonly object m_gate = new();
    private readonly Dictionary<string, long> m_accounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_disabledLogins = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Row> m_characters = new();
    private readonly Dictionary<Guid, LedgerEntry> m_ledger = new();
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
    ///     When set, the next character created gets this ID instead of the next free one, so a test can name it.
    /// </summary>
    public long? NextCharacterId { get; set; }

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
            m_characters.Add(id, new Row(account, character));
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
                if (isNotLower)
                {
                    row.Level = checkpoint.Level;
                    row.Experience = checkpoint.Experience;
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

    public Task<InventoryResult?> FindOperationAsync(
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            return Task.FromResult(Find(operationId, characterId, rowIds));
        }
    }

    public Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        lock (m_gate)
        {
            IReadOnlyList<string> ids = m_characters.Values
                .SelectMany(row => new[] { row.Job, row.Map }.Concat(row.Items.Select(item => item.ItemDefinitionId)))
                .Distinct()
                .ToList();
            return Task.FromResult(ids);
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
        long? experience = null)
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
            return new InventoryResult(InventoryStatus.TakenByOther, 0, Array.Empty<StoredItem>());
        }

        Row row = m_characters[characterId];
        var rows = new List<StoredItem>();
        foreach (long id in new[] { entry.Item }.Concat(rowIds.Where(id => id != entry.Item)))
        {
            rows.Add(row.Items.SingleOrDefault(item => item.Id == id) ?? new StoredItem(id, string.Empty, 0));
        }

        return new InventoryResult(InventoryStatus.Committed, row.InventoryRevision, rows);
    }

    private InventoryResult Commit(PickupCommit pickup)
    {
        Row row = m_characters[pickup.CharacterId];
        int index = row.Items.FindIndex(item => item.ItemDefinitionId == pickup.ItemDefinitionId);
        int held = index >= 0 ? row.Items[index].Quantity : 0;
        if (held > pickup.StackLimit - pickup.Amount || (index < 0 && row.Items.Count >= pickup.MaxRows))
        {
            return new InventoryResult(InventoryStatus.InventoryFull, row.InventoryRevision, Array.Empty<StoredItem>());
        }

        StoredItem stack = index >= 0
            ? new StoredItem(row.Items[index].Id, pickup.ItemDefinitionId, held + pickup.Amount)
            : new StoredItem(++m_lastItem, pickup.ItemDefinitionId, pickup.Amount);
        if (index >= 0)
        {
            row.Items[index] = stack;
        }
        else
        {
            row.Items.Add(stack);
        }

        row.InventoryRevision = unchecked(row.InventoryRevision + 1);
        m_ledger.Add(pickup.DropId, new LedgerEntry(pickup.CharacterId, stack.Id));
        return new InventoryResult(InventoryStatus.Committed, row.InventoryRevision, new[] { stack });
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

    private sealed class LedgerEntry
    {
        public LedgerEntry(long character, long item)
        {
            Character = character;
            Item = item;
        }

        public long Character { get; }

        public long Item { get; }
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

        public uint InventoryRevision { get; set; }

        public List<StoredItem> Items { get; } = new();

        public StoredCharacter ToStored(long id)
        {
            return new StoredCharacter(
                id,
                Account,
                Name,
                Job,
                Level,
                Experience,
                Created.Stats,
                Health,
                Spirit,
                Map,
                Position,
                InventoryRevision,
                Items.ToList());
        }
    }
}
}
