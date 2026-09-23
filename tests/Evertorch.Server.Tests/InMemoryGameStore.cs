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
    private long m_lastAccount;
    private long m_lastCharacter;
    private long m_lastItem;

    public IReadOnlyList<string> PendingMigrations { get; set; } = Array.Empty<string>();

    public bool IsUnavailable { get; set; }

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
        ThrowIfUnavailable();
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
            }
        }

        return Task.CompletedTask;
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
        string? item = null)
    {
        lock (m_gate)
        {
            Row row = m_characters[id];
            row.Job = job ?? row.Job;
            row.Map = map ?? row.Map;
            row.Position = position ?? row.Position;
            row.Health = health ?? row.Health;
            if (item != null)
            {
                row.Items.Add(new StoredItem(++m_lastItem, item, 1));
            }
        }
    }

    public void Disable(string loginNormalized)
    {
        lock (m_gate)
        {
            m_disabledLogins.Add(loginNormalized);
        }
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
        }

        public AccountId Account { get; }

        public NewCharacter Created { get; }

        public string Name { get; }

        public string Job { get; set; }

        public string Map { get; set; }

        public WorldPosition Position { get; set; }

        public int Health { get; set; }

        public uint InventoryRevision { get; set; }

        public List<StoredItem> Items { get; } = new();

        public StoredCharacter ToStored(long id)
        {
            return new StoredCharacter(
                id,
                Account,
                Name,
                Job,
                1,
                Created.Stats,
                Health,
                Map,
                Position,
                InventoryRevision,
                Items.ToList());
        }
    }
}
}
