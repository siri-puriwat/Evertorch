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

    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="operationTimeout">
    ///     When given, the longest any one connection may take to open or to run a command, whatever the connection
    ///     string says.
    /// </param>
    public PostgresGameStore(string connectionString, TimeSpan? operationTimeout = null)
    {
        if (!EvertorchDatabase.TryValidateConnectionString(connectionString, out string error))
        {
            throw new ArgumentException($"The connection string {error}.", nameof(connectionString));
        }

        m_connectionString = operationTimeout is TimeSpan bound ? Bounded(connectionString, bound) : connectionString;
        m_options = EvertorchDatabase.CreateOptions(m_connectionString);
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
                    .Select(item => new StoredItem(
                        item.Id,
                        item.ItemDefinitionId,
                        item.Quantity,
                        context.Equipment
                            .Where(worn => worn.InventoryItemId == item.Id)
                            .Select(worn => worn.Slot)
                            .FirstOrDefault()))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new StoredCharacter(
                    row.Id,
                    account,
                    row.Name,
                    row.JobDefinitionId,
                    row.BaseLevel,
                    row.BaseExp,
                    new PrimaryStats(row.Str, row.Agi, row.Vit, row.Int, row.Dex, row.Luk),
                    row.Hp,
                    row.Sp,
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
                int spirit = checkpoint.Spirit;
                int level = checkpoint.Level;
                long experience = checkpoint.Experience;
                DateTime at = checkpoint.At;

                // One statement: level and experience move only forward, compared as a pair, so a checkpoint queued
                // before a quest reward commits its experience can never take it back (Persistence §6).
                return await context.Characters
                    .Where(row => row.Id == checkpoint.CharacterId)
                    .ExecuteUpdateAsync(
                        update => update
                            .SetProperty(row => row.MapDefinitionId, map)
                            .SetProperty(row => row.PositionX, x)
                            .SetProperty(row => row.PositionY, y)
                            .SetProperty(row => row.PositionZ, z)
                            .SetProperty(row => row.Hp, health)
                            .SetProperty(row => row.Sp, spirit)
                            .SetProperty(
                                row => row.BaseLevel,
                                row => level > row.BaseLevel || (level == row.BaseLevel && experience >= row.BaseExp)
                                    ? level
                                    : row.BaseLevel)
                            .SetProperty(
                                row => row.BaseExp,
                                row => level > row.BaseLevel || (level == row.BaseLevel && experience >= row.BaseExp)
                                    ? experience
                                    : row.BaseExp)
                            .SetProperty(row => row.LastPlayedAt, at)
                            .SetProperty(row => row.Version, row => row.Version + 1),
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken)
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
                    InventoryResult? earlier = await FindAsync(
                        context,
                        pickup.DropId,
                        pickup.CharacterId,
                        Array.Empty<long>(),
                        cancellationToken).ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    List<InventoryItemRow> rows = await context.InventoryItems
                        .Where(row => row.CharacterId == pickup.CharacterId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    // An item with a stack limit of 1 takes a row per unit; any other merges into its one row.
                    InventoryItemRow? stack = pickup.StackLimit == 1
                        ? null
                        : rows.FirstOrDefault(row => row.ItemDefinitionId == pickup.ItemDefinitionId);
                    int held = stack?.Quantity ?? 0;
                    if (held > pickup.StackLimit - pickup.Amount || (stack == null && rows.Count >= pickup.MaxRows))
                    {
                        return new InventoryResult(
                            InventoryStatus.InventoryFull,
                            (uint)character.InventoryRevision,
                            Array.Empty<StoredItem>());
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
                        return await FindAsync(
                                    context,
                                    pickup.DropId,
                                    pickup.CharacterId,
                                    Array.Empty<long>(),
                                    cancellationToken)
                                .ConfigureAwait(false)
                            ?? throw new InvalidOperationException(
                                $"Drop {pickup.DropId} clashed but is not in the ledger.");
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    // A pickup only ever fills an unequipped row: an equipped item's stack limit is 1.
                    return new InventoryResult(
                        InventoryStatus.Committed,
                        (uint)character.InventoryRevision,
                        new[] { new StoredItem(stack.Id, stack.ItemDefinitionId, stack.Quantity) });
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitEquipAsync(EquipCommit equip, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, equip.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            equip.OperationId,
                            equip.CharacterId,
                            equip.ReportRows,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    InventoryItemRow? row = await context.InventoryItems
                        .SingleOrDefaultAsync(
                            item => item.Id == equip.InventoryItemId && item.CharacterId == equip.CharacterId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    EquipmentRow? worn = await context.Equipment
                        .SingleOrDefaultAsync(
                            slot => slot.CharacterId == equip.CharacterId && slot.Slot == equip.Slot,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (row == null || row.Quantity < 1 || worn?.InventoryItemId == row.Id)
                    {
                        return Refused(character);
                    }

                    long? displaced = worn?.InventoryItemId;
                    if (worn == null)
                    {
                        context.Equipment.Add(
                            new EquipmentRow
                                { CharacterId = equip.CharacterId, Slot = equip.Slot, InventoryItemId = row.Id });
                    }
                    else
                    {
                        worn.InventoryItemId = row.Id;
                        worn.Version++;
                    }

                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = equip.OperationId,
                                ActorCharacterId = equip.CharacterId,
                                OperationType = LedgerRow.EquipOperation,
                                ItemInstanceId = row.Id,
                                ItemDefinitionId = row.ItemDefinitionId,
                                CreatedAt = equip.At
                            },
                            displaced.HasValue ? new[] { row.Id, displaced.Value } : new[] { row.Id },
                            equip.ReportRows,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitUnequipAsync(UnequipCommit unequip, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, unequip.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            unequip.OperationId,
                            unequip.CharacterId,
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    EquipmentRow? worn = await context.Equipment
                        .SingleOrDefaultAsync(
                            slot => slot.CharacterId == unequip.CharacterId && slot.Slot == unequip.Slot,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (worn == null)
                    {
                        return Refused(character);
                    }

                    string? item = await context.InventoryItems
                        .Where(row => row.Id == worn.InventoryItemId)
                        .Select(row => row.ItemDefinitionId)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    context.Equipment.Remove(worn);
                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = unequip.OperationId,
                                ActorCharacterId = unequip.CharacterId,
                                OperationType = LedgerRow.UnequipOperation,
                                ItemInstanceId = worn.InventoryItemId,
                                ItemDefinitionId = item,
                                CreatedAt = unequip.At
                            },
                            new[] { worn.InventoryItemId },
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitConsumeAsync(ConsumeCommit consume, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, consume.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            consume.OperationId,
                            consume.CharacterId,
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    InventoryItemRow? row = await context.InventoryItems
                        .SingleOrDefaultAsync(
                            item => item.Id == consume.InventoryItemId && item.CharacterId == consume.CharacterId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (row == null || row.Quantity < 1)
                    {
                        return Refused(character);
                    }

                    // The last unit deletes the row, which the quantity check would refuse to keep at 0.
                    if (row.Quantity == 1)
                    {
                        context.InventoryItems.Remove(row);
                    }
                    else
                    {
                        row.Quantity--;
                        row.Version++;
                    }

                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = consume.OperationId,
                                ActorCharacterId = consume.CharacterId,
                                OperationType = LedgerRow.ConsumeOperation,
                                ItemInstanceId = row.Id,
                                ItemDefinitionId = row.ItemDefinitionId,
                                QuantityDelta = -1,
                                CreatedAt = consume.At
                            },
                            new[] { row.Id },
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult?> FindOperationAsync(
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                // A commit whose answer was lost may still hold the character's lock; waiting for it means the
                // ledger read after it sees that commit or its rollback, never a commit still in flight.
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    await LockCharacterAsync(context, characterId, cancellationToken).ConfigureAwait(false);
                    InventoryResult? found = await FindAsync(
                            context,
                            operationId,
                            characterId,
                            rowIds,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return found;
                }
            },
            cancellationToken);
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

    // A frozen server answers neither a query nor its cancellation. Npgsql then waits 15 s to open a connection and
    // far longer for a cancelled command, so a cancellation token alone does not bound the operation; its own
    // timeouts do, and a cancellation breaks the connection at once instead of waiting for the server's reply.
    private static string Bounded(string connectionString, TimeSpan bound)
    {
        int seconds = (int)Math.Max(1.0, Math.Ceiling(bound.TotalSeconds));
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = seconds,
            CommandTimeout = seconds,
            CancellationTimeout = -1
        };
        return builder.ConnectionString;
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

    private static InventoryResult Refused(CharacterRow character)
    {
        return new InventoryResult(
            InventoryStatus.Refused,
            (uint)character.InventoryRevision,
            Array.Empty<StoredItem>());
    }

    // The common end of an operation other than a pickup: the revision goes up by one, the ledger row goes in with
    // zero deltas unless it says otherwise, and the answer reads the changed rows back. A clash on the operation ID
    // means the same operation won elsewhere; the ledger answers then.
    private static async Task<InventoryResult> CommitOperationAsync(
        EvertorchDbContext context,
        IDbContextTransaction transaction,
        CharacterRow character,
        LedgerRow entry,
        IReadOnlyList<long> changedRows,
        IReadOnlyCollection<long> reportRows,
        CancellationToken cancellationToken)
    {
        character.InventoryRevision = (character.InventoryRevision + 1) % RevisionModulus;
        character.Version++;
        context.Ledger.Add(entry);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, LedgerIndex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            return await FindAsync(context, entry.OperationId, character.Id, reportRows, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Operation {entry.OperationId} clashed but is not in the ledger.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StoredItem> rows = await ReadRowsAsync(
                context,
                character.Id,
                changedRows,
                entry.ItemDefinitionId ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        return new InventoryResult(InventoryStatus.Committed, (uint)character.InventoryRevision, rows);
    }

    private static async Task<InventoryResult?> FindAsync(
        EvertorchDbContext context,
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken)
    {
        LedgerRow? entry = await context.Ledger
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.OperationId == operationId, cancellationToken)
            .ConfigureAwait(false);
        if (entry == null)
        {
            return null;
        }

        if (entry.ActorCharacterId != characterId)
        {
            return new InventoryResult(InventoryStatus.TakenByOther, 0, Array.Empty<StoredItem>());
        }

        long revision = await context.Characters
            .Where(row => row.Id == characterId)
            .Select(row => row.InventoryRevision)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var reported = new List<long>();
        if (entry.ItemInstanceId.HasValue)
        {
            reported.Add(entry.ItemInstanceId.Value);
        }

        reported.AddRange(rowIds.Where(id => !reported.Contains(id)));
        IReadOnlyList<StoredItem> rows = await ReadRowsAsync(
                context,
                characterId,
                reported,
                entry.ItemDefinitionId ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        return new InventoryResult(InventoryStatus.Committed, (uint)revision, rows);
    }

    // The rows as they are now, each with its equipped slot. A row that no longer exists was emptied, so it comes back
    // with a quantity of 0; only the ledger's own row can be emptied, and the ledger names its item.
    private static async Task<IReadOnlyList<StoredItem>> ReadRowsAsync(
        EvertorchDbContext context,
        long characterId,
        IReadOnlyList<long> ids,
        string emptiedItemDefinitionId,
        CancellationToken cancellationToken)
    {
        List<InventoryItemRow> found = await context.InventoryItems
            .AsNoTracking()
            .Where(row => row.CharacterId == characterId && ids.Contains(row.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<EquipmentRow> slots = await context.Equipment
            .AsNoTracking()
            .Where(row => row.CharacterId == characterId && ids.Contains(row.InventoryItemId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rows = new List<StoredItem>(ids.Count);
        foreach (long id in ids)
        {
            InventoryItemRow? row = found.FirstOrDefault(candidate => candidate.Id == id);
            string? slot = slots.FirstOrDefault(candidate => candidate.InventoryItemId == id)?.Slot;
            rows.Add(
                row == null
                    ? new StoredItem(id, emptiedItemDefinitionId, 0)
                    : new StoredItem(row.Id, row.ItemDefinitionId, row.Quantity, slot));
        }

        return rows;
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
