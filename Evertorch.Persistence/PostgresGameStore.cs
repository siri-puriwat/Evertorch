using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
