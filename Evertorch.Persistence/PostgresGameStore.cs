using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
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

    public Task<AccountId?> CreateAccountAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                List<long> created = await context.Database
                    .SqlQuery<long>(
                        $@"INSERT INTO accounts (login_normalized, password_hash, password_scheme, status, created_at)
VALUES ({loginNormalized}, {passwordHash}, {passwordScheme}, {AccountStatus.Active}, {now})
ON CONFLICT (login_normalized) DO NOTHING
RETURNING id AS ""Value""")
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                AccountId? account = null;
                if (created.Count == 1)
                {
                    account = new AccountId(created[0]);
                }

                return account;
            },
            cancellationToken);
    }

    public Task<AccountId?> SetAccountPasswordAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    List<long> changed = await context.Database
                        .SqlQuery<long>(
                            $@"UPDATE accounts SET password_hash = {passwordHash}, password_scheme = {passwordScheme}
WHERE login_normalized = {loginNormalized}
RETURNING id AS ""Value""")
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    AccountId? account = null;
                    if (changed.Count == 1)
                    {
                        account = new AccountId(changed[0]);
                        await context.Database
                            .ExecuteSqlAsync(
                                $"DELETE FROM session_tokens WHERE account_id = {changed[0]}",
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return account;
                }
            },
            cancellationToken);
    }

    public Task<AccountCredentials?> FindAccountCredentialsAsync(
        string loginNormalized,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                AccountRow? row = await context.Accounts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(account => account.LoginNormalized == loginNormalized, cancellationToken)
                    .ConfigureAwait(false);
                return row == null
                    ? null
                    : new AccountCredentials(
                        new AccountId(row.Id),
                        row.PasswordScheme,
                        row.PasswordHash,
                        row.Status != AccountStatus.Active);
            },
            cancellationToken);
    }

    public Task IssueSessionTokenAsync(
        AccountId account,
        byte[] tokenHash,
        DateTime issuedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        RequireTokenHash(tokenHash);
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Sign-ins of one account take turns, so none sees the other's token while keeping the newest.
                    await LockAccountAsync(context, account, cancellationToken).ConfigureAwait(false);
                    context.SessionTokens.Add(
                        new SessionTokenRow
                        {
                            TokenHash = tokenHash,
                            AccountId = account.Value,
                            IssuedAt = issuedAt,
                            ExpiresAt = expiresAt
                        });
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await context.Database
                        .ExecuteSqlAsync(
                            $"UPDATE accounts SET last_login_at = {issuedAt} WHERE id = {account.Value}",
                            cancellationToken)
                        .ConfigureAwait(false);
                    DateTime forgotten = issuedAt - SessionTokenLimits.ExpiredRetention;
                    await context.Database
                        .ExecuteSqlAsync($"DELETE FROM session_tokens WHERE expires_at < {forgotten}",
                            cancellationToken)
                        .ConfigureAwait(false);
                    await context.Database
                        .ExecuteSqlAsync(
                            $@"DELETE FROM session_tokens
WHERE account_id = {account.Value} AND expires_at > {issuedAt} AND token_hash NOT IN (
    SELECT token_hash FROM session_tokens
    WHERE account_id = {account.Value} AND expires_at > {issuedAt}
    ORDER BY issued_at DESC, token_hash DESC
    LIMIT {SessionTokenLimits.MaxLiveTokensPerAccount})",
                            cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                }
            },
            cancellationToken);
    }

    public Task<StoredSessionToken?> FindSessionTokenAsync(byte[] tokenHash, CancellationToken cancellationToken)
    {
        RequireTokenHash(tokenHash);
        return RunAsync(
            async context =>
            {
                var found = await context.SessionTokens
                    .AsNoTracking()
                    .Where(token => token.TokenHash == tokenHash)
                    .Join(
                        context.Accounts,
                        token => token.AccountId,
                        account => account.Id,
                        (token, account) => new { token.AccountId, token.ExpiresAt, account.Status })
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                return found == null
                    ? null
                    : new StoredSessionToken(
                        new AccountId(found.AccountId),
                        found.ExpiresAt,
                        found.Status != AccountStatus.Active);
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
                List<StoredQuest> quests = await context.CharacterQuests
                    .AsNoTracking()
                    .Where(quest => quest.CharacterId == characterId)
                    .OrderBy(quest => quest.QuestDefinitionId)
                    .Select(quest => new StoredQuest(
                        quest.QuestDefinitionId,
                        quest.State == CharacterQuestRow.CompletedState,
                        quest.Progress))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                List<StoredSkill> skills = await context.CharacterSkills
                    .AsNoTracking()
                    .Where(skill => skill.CharacterId == characterId)
                    .OrderBy(skill => skill.SkillDefinitionId)
                    .Select(skill => new StoredSkill(skill.SkillDefinitionId, skill.Level))
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
                    row.Currency,
                    items,
                    quests,
                    row.JobLevel,
                    row.JobExp,
                    skills);
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
                bool isCommitInFlight = checkpoint.IsCommitInFlight;
                string job = checkpoint.Job;
                int jobLevel = checkpoint.JobLevel;
                long jobExperience = checkpoint.JobExperience;
                bool writesStats = !isCommitInFlight && checkpoint.Stats.HasValue;
                PrimaryStats stats = checkpoint.Stats.GetValueOrDefault();
                int str = stats.Str;
                int agi = stats.Agi;
                int vit = stats.Vit;
                int intelligence = stats.Int;
                int dex = stats.Dex;
                int luk = stats.Luk;
                DateTime at = checkpoint.At;
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Level and experience move only forward, compared as a pair, so a checkpoint can never take back
                    // what a turn-in committed; while a turn-in or a job change is in flight they stay as they are,
                    // since the ones in memory may not hold what it commits yet (Persistence §6). The job pair follows
                    // the same rule, and only while the stored job is the checkpoint's, so a checkpoint taken before a
                    // change can never store a first job at its base job's level. The statistics and skills, which spend
                    // the points those levels grant, wait with them, so a crash can never store more points spent than
                    // earned.
                    int updated = await context.Characters
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
                                    row => !isCommitInFlight
                                        && (level > row.BaseLevel
                                            || (level == row.BaseLevel && experience >= row.BaseExp))
                                            ? level
                                            : row.BaseLevel)
                                .SetProperty(
                                    row => row.BaseExp,
                                    row => !isCommitInFlight
                                        && (level > row.BaseLevel
                                            || (level == row.BaseLevel && experience >= row.BaseExp))
                                            ? experience
                                            : row.BaseExp)
                                .SetProperty(
                                    row => row.JobLevel,
                                    row => !isCommitInFlight
                                        && row.JobDefinitionId == job
                                        && (jobLevel > row.JobLevel
                                            || (jobLevel == row.JobLevel && jobExperience >= row.JobExp))
                                            ? jobLevel
                                            : row.JobLevel)
                                .SetProperty(
                                    row => row.JobExp,
                                    row => !isCommitInFlight
                                        && row.JobDefinitionId == job
                                        && (jobLevel > row.JobLevel
                                            || (jobLevel == row.JobLevel && jobExperience >= row.JobExp))
                                            ? jobExperience
                                            : row.JobExp)
                                .SetProperty(row => row.Str, row => writesStats ? str : row.Str)
                                .SetProperty(row => row.Agi, row => writesStats ? agi : row.Agi)
                                .SetProperty(row => row.Vit, row => writesStats ? vit : row.Vit)
                                .SetProperty(row => row.Int, row => writesStats ? intelligence : row.Int)
                                .SetProperty(row => row.Dex, row => writesStats ? dex : row.Dex)
                                .SetProperty(row => row.Luk, row => writesStats ? luk : row.Luk)
                                .SetProperty(row => row.LastPlayedAt, at)
                                .SetProperty(row => row.Version, row => row.Version + 1),
                            cancellationToken)
                        .ConfigureAwait(false);

                    // Forward only: an active quest's progress only rises, and a completed quest, which only its
                    // turn-in writes, is never reopened (Persistence §6).
                    foreach (StoredQuest quest in checkpoint.Quests)
                    {
                        await context.Database
                            .ExecuteSqlInterpolatedAsync(
                                $@"INSERT INTO character_quests
    (character_id, quest_definition_id, state, progress, started_at, completed_at, version)
VALUES ({checkpoint.CharacterId}, {quest.QuestDefinitionId}, {CharacterQuestRow.ActiveState}, {quest.Progress}, {at},
    NULL, 0)
ON CONFLICT (character_id, quest_definition_id) DO UPDATE
SET progress = EXCLUDED.progress, version = character_quests.version + 1
WHERE character_quests.state = {CharacterQuestRow.ActiveState} AND character_quests.progress < EXCLUDED.progress",
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!isCommitInFlight && checkpoint.Skills != null && updated == 1)
                    {
                        await WriteSkillsAsync(context, checkpoint.CharacterId, checkpoint.Skills, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return updated;
                }
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
                            character.Currency,
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
                        character.Currency,
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

    public Task<InventoryResult> CommitBuyAsync(BuyCommit buy, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, buy.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            buy.OperationId,
                            buy.CharacterId,
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    if (character.Currency < buy.Total)
                    {
                        return Refused(character);
                    }

                    List<InventoryItemRow> rows = await context.InventoryItems
                        .Where(row => row.CharacterId == buy.CharacterId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    InventoryItemRow? stack = buy.StackLimit == 1
                        ? null
                        : rows.FirstOrDefault(row => row.ItemDefinitionId == buy.ItemDefinitionId);
                    int held = stack?.Quantity ?? 0;
                    if (held > buy.StackLimit - buy.Quantity || (stack == null && rows.Count >= buy.MaxRows))
                    {
                        return new InventoryResult(
                            InventoryStatus.InventoryFull,
                            (uint)character.InventoryRevision,
                            character.Currency,
                            Array.Empty<StoredItem>());
                    }

                    if (stack == null)
                    {
                        // Saved at once, so the ledger row can name it.
                        stack = new InventoryItemRow
                        {
                            CharacterId = buy.CharacterId,
                            ItemDefinitionId = buy.ItemDefinitionId,
                            Quantity = buy.Quantity
                        };
                        context.InventoryItems.Add(stack);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        stack.Quantity += buy.Quantity;
                        stack.Version++;
                    }

                    character.Currency -= buy.Total;
                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = buy.OperationId,
                                ActorCharacterId = buy.CharacterId,
                                OperationType = LedgerRow.BuyOperation,
                                ItemInstanceId = stack.Id,
                                ItemDefinitionId = buy.ItemDefinitionId,
                                QuantityDelta = buy.Quantity,
                                CurrencyDelta = -buy.Total,
                                CreatedAt = buy.At
                            },
                            new[] { stack.Id },
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitSellAsync(SellCommit sell, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, sell.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            sell.OperationId,
                            sell.CharacterId,
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    InventoryItemRow? row = await context.InventoryItems
                        .SingleOrDefaultAsync(
                            item => item.Id == sell.InventoryItemId && item.CharacterId == sell.CharacterId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    bool isWorn = await context.Equipment
                        .AnyAsync(slot => slot.InventoryItemId == sell.InventoryItemId, cancellationToken)
                        .ConfigureAwait(false);
                    if (row == null
                        || isWorn
                        || row.Quantity < sell.Quantity
                        || character.Currency > EvertorchDbContext.MaxCurrency - sell.Proceeds)
                    {
                        return Refused(character);
                    }

                    // A row sold whole is deleted, which the quantity check would refuse to keep at 0.
                    if (row.Quantity == sell.Quantity)
                    {
                        context.InventoryItems.Remove(row);
                    }
                    else
                    {
                        row.Quantity -= sell.Quantity;
                        row.Version++;
                    }

                    character.Currency += sell.Proceeds;
                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = sell.OperationId,
                                ActorCharacterId = sell.CharacterId,
                                OperationType = LedgerRow.SellOperation,
                                ItemInstanceId = row.Id,
                                ItemDefinitionId = row.ItemDefinitionId,
                                QuantityDelta = -sell.Quantity,
                                CurrencyDelta = sell.Proceeds,
                                CreatedAt = sell.At
                            },
                            new[] { row.Id },
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitQuestRewardAsync(QuestRewardCommit reward, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, reward.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? earlier = await FindAsync(
                            context,
                            reward.OperationId,
                            reward.CharacterId,
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (earlier != null)
                    {
                        return earlier;
                    }

                    CharacterQuestRow? quest = await context.CharacterQuests
                        .SingleOrDefaultAsync(
                            row => row.CharacterId == reward.CharacterId
                                && row.QuestDefinitionId == reward.QuestDefinitionId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (quest?.State == CharacterQuestRow.CompletedState
                        || reward.Progress < reward.Count
                        || character.Currency > EvertorchDbContext.MaxCurrency - reward.Coins)
                    {
                        return Refused(character);
                    }

                    // An acceptance whose checkpoint was never written left no row; the turn-in adds it.
                    if (quest == null)
                    {
                        quest = new CharacterQuestRow
                        {
                            CharacterId = reward.CharacterId,
                            QuestDefinitionId = reward.QuestDefinitionId,
                            StartedAt = reward.At
                        };
                        context.CharacterQuests.Add(quest);
                    }
                    else
                    {
                        quest.Version++;
                    }

                    quest.State = CharacterQuestRow.CompletedState;
                    quest.Progress = Math.Max(quest.Progress, reward.Count);
                    quest.CompletedAt = reward.At;
                    character.Currency += reward.Coins;
                    if (reward.Level > character.BaseLevel
                        || (reward.Level == character.BaseLevel && reward.Experience >= character.BaseExp))
                    {
                        character.BaseLevel = reward.Level;
                        character.BaseExp = reward.Experience;
                    }

                    if (character.JobDefinitionId == reward.Job
                        && (reward.JobLevel > character.JobLevel
                            || (reward.JobLevel == character.JobLevel && reward.JobExperience >= character.JobExp)))
                    {
                        character.JobLevel = reward.JobLevel;
                        character.JobExp = reward.JobExperience;
                    }

                    return await CommitOperationAsync(
                            context,
                            transaction,
                            character,
                            new LedgerRow
                            {
                                OperationId = reward.OperationId,
                                ActorCharacterId = reward.CharacterId,
                                OperationType = LedgerRow.QuestRewardOperation,
                                CurrencyDelta = reward.Coins,
                                MetadataJson = JsonSerializer.Serialize(
                                    new Dictionary<string, string> { ["quest"] = reward.QuestDefinitionId }),
                                CreatedAt = reward.At
                            },
                            Array.Empty<long>(),
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult> CommitJobChangeAsync(JobChangeCommit change, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, change.CharacterId, cancellationToken)
                        .ConfigureAwait(false);
                    if (character.JobDefinitionId == change.ToJob)
                    {
                        return await ChangedAsync(context, character, change.OperationId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (character.JobDefinitionId != change.FromJob)
                    {
                        return Refused(character);
                    }

                    // The job is not value, so the change alone keeps no ledger row; an item it takes off does.
                    character.JobDefinitionId = change.ToJob;
                    character.JobLevel = StartingLevel;
                    character.JobExp = 0;
                    EquipmentRow? worn = change.UnequipSlot == null
                        ? null
                        : await context.Equipment
                            .SingleOrDefaultAsync(
                                slot => slot.CharacterId == change.CharacterId && slot.Slot == change.UnequipSlot,
                                cancellationToken)
                            .ConfigureAwait(false);
                    if (worn == null)
                    {
                        character.Version++;
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return new InventoryResult(
                            InventoryStatus.Committed,
                            (uint)character.InventoryRevision,
                            character.Currency,
                            Array.Empty<StoredItem>());
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
                                OperationId = change.OperationId,
                                ActorCharacterId = change.CharacterId,
                                OperationType = LedgerRow.UnequipOperation,
                                ItemInstanceId = worn.InventoryItemId,
                                ItemDefinitionId = item,
                                CreatedAt = change.At
                            },
                            new[] { worn.InventoryItemId },
                            Array.Empty<long>(),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task<StoredParty?> LoadPartyAsync(long characterId, CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                // One snapshot for the party and its members, so a change committed between two reads is never half
                // seen.
                await using (IDbContextTransaction transaction = await context.Database
                                 .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
                                 .ConfigureAwait(false))
                {
                    StoredParty? party = await ReadPartyAsync(context, characterId, cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return party;
                }
            },
            cancellationToken);
    }

    public Task<InventoryResult?> FindJobChangeAsync(
        Guid operationId,
        long characterId,
        string job,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            async context =>
            {
                // As for an operation's lookup: waiting on the lock means a commit still in flight is seen settled.
                await using (IDbContextTransaction transaction =
                             await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                {
                    CharacterRow character = await LockCharacterAsync(context, characterId, cancellationToken)
                        .ConfigureAwait(false);
                    InventoryResult? found = character.JobDefinitionId == job
                        ? await ChangedAsync(context, character, operationId, cancellationToken).ConfigureAwait(false)
                        : null;
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return found;
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
                List<string> quests = await context.CharacterQuests.Select(row => row.QuestDefinitionId).Distinct()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                List<string> skills = await context.CharacterSkills.Select(row => row.SkillDefinitionId).Distinct()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<string>)jobs.Concat(maps).Concat(items).Concat(quests).Concat(skills).Distinct()
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
            },
            cancellationToken);
    }

    private static async Task<StoredParty?> ReadPartyAsync(
        EvertorchDbContext context,
        long characterId,
        CancellationToken cancellationToken)
    {
        PartyRow? party = await context.PartyMembers
            .AsNoTracking()
            .Where(member => member.CharacterId == characterId)
            .Join(context.Parties.AsNoTracking(), member => member.PartyId, row => row.Id, (_, row) => row)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (party == null)
        {
            return null;
        }

        var members = await context.PartyMembers
            .AsNoTracking()
            .Where(member => member.PartyId == party.Id)
            .Join(
                context.Characters.AsNoTracking(),
                member => member.CharacterId,
                character => character.Id,
                (member, character) => new
                {
                    member.CharacterId,
                    character.Name,
                    character.JobDefinitionId,
                    character.BaseLevel,
                    character.AccountId,
                    member.JoinOrder
                })
            .OrderBy(member => member.JoinOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new StoredParty(
            party.Id,
            party.LeaderCharacterId,
            members.Select(member => new StoredPartyMember(
                    member.CharacterId,
                    member.Name,
                    member.JobDefinitionId,
                    member.BaseLevel,
                    new AccountId(member.AccountId),
                    member.JoinOrder))
                .ToList());
    }

    // Written whole: each learned skill is inserted or moved to its level, and a stored skill left out is forgotten, so
    // a reset's lowered build is stored as it is (Persistence §6).
    private static async Task WriteSkillsAsync(
        EvertorchDbContext context,
        long characterId,
        IReadOnlyList<StoredSkill> skills,
        CancellationToken cancellationToken)
    {
        string[] learned = skills.Select(skill => skill.SkillDefinitionId).ToArray();
        await context.Database
            .ExecuteSqlInterpolatedAsync(
                $"DELETE FROM character_skills WHERE character_id = {characterId} AND skill_definition_id <> ALL ({learned})",
                cancellationToken)
            .ConfigureAwait(false);
        foreach (StoredSkill skill in skills)
        {
            await context.Database
                .ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO character_skills (character_id, skill_definition_id, level)
VALUES ({characterId}, {skill.SkillDefinitionId}, {skill.Level})
ON CONFLICT (character_id, skill_definition_id) DO UPDATE SET level = EXCLUDED.level
WHERE character_skills.level <> EXCLUDED.level",
                    cancellationToken)
                .ConfigureAwait(false);
        }
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

    // A job change that committed: the ledger's answer when it took an item off, else the character as it is.
    private static async Task<InventoryResult> ChangedAsync(
        EvertorchDbContext context,
        CharacterRow character,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return await FindAsync(context, operationId, character.Id, Array.Empty<long>(), cancellationToken)
                .ConfigureAwait(false)
            ?? new InventoryResult(
                InventoryStatus.Committed,
                (uint)character.InventoryRevision,
                character.Currency,
                Array.Empty<StoredItem>());
    }

    private static InventoryResult Refused(CharacterRow character)
    {
        return new InventoryResult(
            InventoryStatus.Refused,
            (uint)character.InventoryRevision,
            character.Currency,
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
        return new InventoryResult(
            InventoryStatus.Committed,
            (uint)character.InventoryRevision,
            character.Currency,
            rows);
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
            return new InventoryResult(InventoryStatus.TakenByOther, 0, 0, Array.Empty<StoredItem>());
        }

        var now = await context.Characters
            .Where(row => row.Id == characterId)
            .Select(row => new { row.InventoryRevision, row.Currency })
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
        return new InventoryResult(InventoryStatus.Committed, (uint)now.InventoryRevision, now.Currency, rows);
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

    private static void RequireTokenHash(byte[] tokenHash)
    {
        if (tokenHash == null || tokenHash.Length != SessionTokenLimits.HashLength)
        {
            throw new ArgumentException(
                $"A token hash is {SessionTokenLimits.HashLength} bytes.",
                nameof(tokenHash));
        }
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
