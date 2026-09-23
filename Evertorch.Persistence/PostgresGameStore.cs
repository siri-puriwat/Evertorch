using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Evertorch.Persistence
{
/// <summary>
///     <see cref="IGameStore" /> on PostgreSQL through EF Core. Every call opens its own context, so the store is safe
///     to share, but the server still calls it from one writer at a time (Persistence §9).
/// </summary>
public sealed class PostgresGameStore : IGameStore
{
    private const int StartingLevel = 1;
    private const string NameIndex = "ux_characters_name_normalized";
    private const string LedgerIndex = "ux_economy_ledger_operation_id";

    // The inventory revision is an unsigned 32-bit number stored in a bigint; it wraps to 0.
    private const long RevisionModulus = 1L << 32;

    private readonly string m_connectionString;
    private readonly DbContextOptions<EvertorchDbContext> m_options;

    public PostgresGameStore(string connectionString)
    {
        if (!EvertorchDatabase.TryValidateConnectionString(connectionString, out string error))
        {
            throw new ArgumentException($"The connection string {error}.", nameof(connectionString));
        }

        m_connectionString = connectionString;
        m_options = EvertorchDatabase.CreateOptions(connectionString);
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        return EvertorchDatabase.GetPendingMigrationsAsync(m_connectionString, cancellationToken);
    }

    public Task<AccountId?> ProvisionAccountAsync(
        string loginNormalized,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                // One statement, so two first logins of the same identity cannot create two accounts.
                List<AccountLogin> rows = await context.Database
                    .SqlQuery<AccountLogin>(
                        $@"INSERT INTO accounts (login_normalized, status, created_at, last_login_at)
VALUES ({loginNormalized}, {AccountStatus.Active}, {now}, {now})
ON CONFLICT (login_normalized) DO UPDATE SET last_login_at = EXCLUDED.last_login_at
RETURNING id AS ""Id"", status AS ""Status""")
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                AccountLogin login = rows.Single();
                return login.Status == AccountStatus.Active ? new AccountId(login.Id) : (AccountId?)null;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(
        AccountId account,
        CancellationToken cancellationToken)
    {
        return RunAsync(context => ListAsync(context, account, cancellationToken), cancellationToken);
    }

    public Task<CharacterCreation> CreateCharacterAsync(
        AccountId account,
        NewCharacter character,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    await LockAccountAsync(context, account, cancellationToken).ConfigureAwait(false);
                    int count = await context.Characters
                        .CountAsync(row => row.AccountId == account.Value, cancellationToken)
                        .ConfigureAwait(false);
                    if (count >= maxCharacters)
                    {
                        return new CharacterCreation(
                            CharacterCreationStatus.LimitReached,
                            0,
                            await ListAsync(context, account, cancellationToken).ConfigureAwait(false));
                    }

                    CharacterRow row = NewRow(account, character);
                    context.Characters.Add(row);
                    try
                    {
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException exception) when (IsUniqueViolation(exception, NameIndex))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        context.ChangeTracker.Clear();
                        return new CharacterCreation(
                            CharacterCreationStatus.NameTaken,
                            0,
                            await ListAsync(context, account, cancellationToken).ConfigureAwait(false));
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new CharacterCreation(
                        CharacterCreationStatus.Created,
                        row.Id,
                        await ListAsync(context, account, cancellationToken).ConfigureAwait(false));
                }
            },
            cancellationToken);
    }

    public Task<StoredCharacter?> LoadCharacterAsync(
        AccountId account,
        long characterId,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                CharacterRow? row = await context.Characters
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        character => character.Id == characterId && character.AccountId == account.Value,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (row == null)
                {
                    return null;
                }

                List<StoredItem> items = await context.InventoryItems
                    .AsNoTracking()
                    .Where(item => item.CharacterId == characterId)
                    .OrderBy(item => item.Id)
                    .Select(item => new StoredItem(item.Id, item.ItemDefinitionId, item.Quantity))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new StoredCharacter(
                    row.Id,
                    account,
                    row.Name,
                    row.JobDefinitionId,
                    row.BaseLevel,
                    new PrimaryStats(row.Str, row.Agi, row.Vit, row.Int, row.Dex, row.Luk),
                    row.Hp,
                    row.MapDefinitionId,
                    new WorldPosition(row.PositionX, row.PositionY, row.PositionZ),
                    (uint)row.InventoryRevision,
                    items);
            },
            cancellationToken);
    }

    public Task SaveCheckpointAsync(CharacterCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                string map = checkpoint.Map.Value;
                float x = checkpoint.Position.X;
                float y = checkpoint.Position.Y;
                float z = checkpoint.Position.Z;
                int health = checkpoint.Health;
                DateTime at = checkpoint.At;
                return await context.Characters
                    .Where(row => row.Id == checkpoint.CharacterId)
                    .ExecuteUpdateAsync(
                        update => update
                            .SetProperty(row => row.MapDefinitionId, map)
                            .SetProperty(row => row.PositionX, x)
                            .SetProperty(row => row.PositionY, y)
                            .SetProperty(row => row.PositionZ, z)
                            .SetProperty(row => row.Hp, health)
                            .SetProperty(row => row.LastPlayedAt, at)
                            .SetProperty(row => row.Version, row => row.Version + 1),
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<PickupResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Locked before the ledger is read, so a retry of the same pickup waits for the first and then
                    // finds it instead of adding the item twice.
                    CharacterRow character = await LockCharacterAsync(context, pickup.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    PickupResult? earlier = await FindAsync(context, pickup.DropId, pickup.CharacterId,
                        cancellationToken).ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    List<InventoryItemRow> rows = await context.InventoryItems
                        .Where(row => row.CharacterId == pickup.CharacterId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    InventoryItemRow? stack =
                        rows.FirstOrDefault(row => row.ItemDefinitionId == pickup.ItemDefinitionId);
                    int held = stack?.Quantity ?? 0;
                    if (held > pickup.StackLimit - pickup.Amount || (stack == null && rows.Count >= pickup.MaxRows))
                    {
                        return new PickupResult(PickupStatus.InventoryFull, (uint)character.InventoryRevision, null);
                    }

                    if (stack == null)
                    {
                        stack = new InventoryItemRow
                        {
                            CharacterId = pickup.CharacterId,
                            ItemDefinitionId = pickup.ItemDefinitionId,
                            Quantity = pickup.Amount
                        };
                        context.InventoryItems.Add(stack);
                    }
                    else
                    {
                        stack.Quantity += pickup.Amount;
                        stack.Version++;
                    }

                    character.InventoryRevision = (character.InventoryRevision + 1) % RevisionModulus;
                    character.Version++;
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    context.Ledger.Add(
                        new LedgerRow
                        {
                            OperationId = pickup.DropId,
                            ActorCharacterId = pickup.CharacterId,
                            OperationType = LedgerRow.PickupOperation,
                            ItemInstanceId = stack.Id,
                            ItemDefinitionId = pickup.ItemDefinitionId,
                            QuantityDelta = pickup.Amount,
                            CreatedAt = pickup.At
                        });
                    try
                    {
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException exception) when (IsUniqueViolation(exception, LedgerIndex))
                    {
                        // Another character's commit of the same drop won the race.
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        context.ChangeTracker.Clear();
                        return await FindAsync(context, pickup.DropId, pickup.CharacterId, cancellationToken)
                                .ConfigureAwait(false)
                            ?? throw new InvalidOperationException(
                                $"Drop {pickup.DropId} clashed but is not in the ledger.");
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new PickupResult(
                        PickupStatus.Committed,
                        (uint)character.InventoryRevision,
                        new StoredItem(stack.Id, stack.ItemDefinitionId, stack.Quantity));
                }
            },
            cancellationToken);
    }

    public Task<PickupResult?> FindPickupAsync(Guid dropId, long characterId, CancellationToken cancellationToken)
    {
        return RunAsync(context => FindAsync(context, dropId, characterId, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                List<string> jobs = await context.Characters.Select(row => row.JobDefinitionId).Distinct()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                List<string> maps = await context.Characters.Select(row => row.MapDefinitionId).Distinct()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                List<string> items = await context.InventoryItems.Select(row => row.ItemDefinitionId).Distinct()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<string>)jobs.Concat(maps).Concat(items).Distinct()
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
            },
            cancellationToken);
    }

    private static CharacterRow NewRow(AccountId account, NewCharacter character)
    {
        return new CharacterRow
        {
            AccountId = account.Value,
            Name = character.Name,
            NameNormalized = character.Name.ToLowerInvariant(),
            JobDefinitionId = character.Job.Value,
            BaseLevel = StartingLevel,
            JobLevel = StartingLevel,
            Str = character.Stats.Str,
            Agi = character.Stats.Agi,
            Vit = character.Stats.Vit,
            Int = character.Stats.Int,
            Dex = character.Stats.Dex,
            Luk = character.Stats.Luk,
            Hp = character.Health,
            Sp = character.Spirit,
            MapDefinitionId = character.Map.Value,
            PositionX = character.Position.X,
            PositionY = character.Position.Y,
            PositionZ = character.Position.Z,
            CreatedAt = character.CreatedAt
        };
    }

    private static async Task<IReadOnlyList<CharacterSummary>> ListAsync(
        EvertorchDbContext context,
        AccountId account,
        CancellationToken cancellationToken)
    {
        return await context.Characters
            .AsNoTracking()
            .Where(row => row.AccountId == account.Value)
            .OrderBy(row => row.Id)
            .Select(row => new CharacterSummary(row.Id, row.Name, row.JobDefinitionId, row.BaseLevel))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Holding the account row makes concurrent creations for one account take turns, so the count stays true.
    private static async Task LockAccountAsync(
        EvertorchDbContext context,
        AccountId account,
        CancellationToken cancellationToken)
    {
        List<long> locked = await context.Database
            .SqlQuery<long>($@"SELECT id AS ""Value"" FROM accounts WHERE id = {account.Value} FOR UPDATE")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (locked.Count == 0)
        {
            throw new InvalidOperationException($"Account {account.Value} does not exist.");
        }
    }

    private static async Task<CharacterRow> LockCharacterAsync(
        EvertorchDbContext context,
        long characterId,
        CancellationToken cancellationToken)
    {
        List<CharacterRow> locked = await context.Characters
            .FromSql($"SELECT * FROM characters WHERE id = {characterId} FOR UPDATE")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return locked.Count == 1
            ? locked[0]
            : throw new InvalidOperationException($"Character {characterId} does not exist.");
    }

    private static async Task<PickupResult?> FindAsync(
        EvertorchDbContext context,
        Guid dropId,
        long characterId,
        CancellationToken cancellationToken)
    {
        LedgerRow? entry = await context.Ledger
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationId == dropId, cancellationToken)
            .ConfigureAwait(false);
        if (entry == null)
        {
            return null;
        }

        if (entry.ActorCharacterId != characterId)
        {
            return new PickupResult(PickupStatus.TakenByOther, 0, null);
        }

        long revision = await context.Characters
            .Where(row => row.Id == characterId)
            .Select(row => row.InventoryRevision)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        StoredItem? item = await context.InventoryItems
            .AsNoTracking()
            .Where(row => row.Id == entry.ItemInstanceId)
            .Select(row => new StoredItem(row.Id, row.ItemDefinitionId, row.Quantity))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PickupResult(PickupStatus.Committed, (uint)revision, item);
    }

    private static bool IsUniqueViolation(DbUpdateException exception, string index)
    {
        return exception.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation
            && postgres.ConstraintName == index;
    }

    private async Task<T> RunAsync<T>(Func<EvertorchDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = new EvertorchDbContext(m_options);
            return await work(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (StoreFailures.IsUnavailable(exception, cancellationToken))
        {
            throw new StoreUnavailableException(exception);
        }
    }

    private sealed class AccountLogin
    {
        public long Id { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
}
