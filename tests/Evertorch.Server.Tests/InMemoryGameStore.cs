using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly Dictionary<long, StoredCharacter> m_characters = new();
    private long m_lastAccount;
    private long m_lastCharacter;

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

            foreach (StoredCharacter stored in m_characters.Values)
            {
                if (string.Equals(stored.Character.Name, character.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new CharacterCreation(CharacterCreationStatus.NameTaken, 0, List(account)));
                }
            }

            long id = NextCharacterId ?? m_lastCharacter + 1;
            NextCharacterId = null;
            m_lastCharacter = Math.Max(m_lastCharacter, id);
            m_characters.Add(id, new StoredCharacter(account, character));
            return Task.FromResult(new CharacterCreation(CharacterCreationStatus.Created, id, List(account)));
        }
    }

    /// <summary>
    ///     The starting values the server chose for character <paramref name="id" />.
    /// </summary>
    public NewCharacter CreatedCharacter(long id)
    {
        lock (m_gate)
        {
            return m_characters[id].Character;
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
        var characters = new List<CharacterSummary>();
        foreach (KeyValuePair<long, StoredCharacter> pair in m_characters)
        {
            if (pair.Value.Account == account)
            {
                characters.Add(new CharacterSummary(pair.Key, pair.Value.Character.Name, pair.Value.Character.Job.Value,
                    1));
            }
        }

        characters.Sort((left, right) => left.Id.CompareTo(right.Id));
        return characters;
    }

    private void ThrowIfUnavailable()
    {
        if (IsUnavailable)
        {
            throw new StoreUnavailableException(new TimeoutException("scripted outage"));
        }
    }

    private sealed class StoredCharacter
    {
        public StoredCharacter(AccountId account, NewCharacter character)
        {
            Account = account;
            Character = character;
        }

        public AccountId Account { get; }

        public NewCharacter Character { get; }
    }
}
}
