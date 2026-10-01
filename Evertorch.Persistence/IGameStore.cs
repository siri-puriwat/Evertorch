using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Evertorch.Persistence
{
/// <summary>
///     Everything the server asks of durable storage. Each call is one unit of work with its own connection, and
///     results are plain values: no database type or tracked entity reaches the simulation.
/// </summary>
/// <remarks>
///     A call that cannot reach the database throws <see cref="StoreUnavailableException" />; retrying it later is
///     safe. Any other exception is a permanent failure of that operation.
/// </remarks>
public interface IGameStore
{
    /// <summary>
    ///     The migrations this build knows that the database has not applied, oldest first.
    /// </summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The account of <paramref name="loginNormalized" />, created on first use (Persistence §4), with its last
    ///     login set to <paramref name="now" />. Null when the account exists but is disabled.
    /// </summary>
    Task<AccountId?> ProvisionAccountAsync(string loginNormalized, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates an account with a password (Persistence §4). Null when <paramref name="loginNormalized" /> is taken.
    /// </summary>
    Task<AccountId?> CreateAccountAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces the account's password and deletes its session tokens, in one transaction. Null when no account has
    ///     <paramref name="loginNormalized" />.
    /// </summary>
    Task<AccountId?> SetAccountPasswordAsync(
        string loginNormalized,
        string passwordScheme,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The account of <paramref name="loginNormalized" /> with its stored password, or null when there is none.
    /// </summary>
    Task<AccountCredentials?> FindAccountCredentialsAsync(
        string loginNormalized,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Stores a new session token of the account and records the sign-in as its last login, under the account's
    ///     lock; deletes every token that expired more than <see cref="SessionTokenLimits.ExpiredRetention" /> before
    ///     <paramref name="issuedAt" />, and the account's live tokens beyond its newest
    ///     <see cref="SessionTokenLimits.MaxLiveTokensPerAccount" />.
    /// </summary>
    Task IssueSessionTokenAsync(
        AccountId account,
        byte[] tokenHash,
        DateTime issuedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The stored token whose hash is <paramref name="tokenHash" />, with its account's status, or null.
    /// </summary>
    Task<StoredSessionToken?> FindSessionTokenAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>
    ///     The account's characters, oldest first.
    /// </summary>
    Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(AccountId account, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates <paramref name="character" /> on the account unless it already holds
    ///     <paramref name="maxCharacters" /> or the name is taken, in one transaction that locks the account, so two
    ///     creations at once cannot pass the limit together.
    /// </summary>
    Task<CharacterCreation> CreateCharacterAsync(
        AccountId account,
        NewCharacter character,
        int maxCharacters,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The character with its inventory and its quests, or null when the account owns no such character.
    /// </summary>
    Task<StoredCharacter?> LoadCharacterAsync(AccountId account, long characterId, CancellationToken cancellationToken);

    /// <summary>
    ///     The party of <paramref name="characterId" />, if it has one: its leader and every member's name, job, base
    ///     level, and account, read in one snapshot (Persistence §7).
    /// </summary>
    Task<StoredParty?> LoadPartyAsync(long characterId, CancellationToken cancellationToken);

    /// <summary>
    ///     Commits one party change in its own transaction under the party's lock (Persistence §5): every refusal
    ///     is a status, and a change whose end state already holds is committed. A departure that leaves one member
    ///     disbands the party, and a leader's departure passes the lead to the earliest joined. No ledger row.
    /// </summary>
    Task<PartyChangeResult> CommitPartyChangeAsync(PartyChange change, CancellationToken cancellationToken);

    /// <summary>
    ///     The party of <paramref name="characterId" /> once every change in flight for it has settled, which tells
    ///     whether a change whose answer was lost took effect (Persistence §5).
    /// </summary>
    Task<StoredParty?> FindPartyStateAsync(long characterId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the checkpoint over the character's map, position, HP, and SP, and its level and experience unless
    ///     they would go down or a turn-in or a job change was in flight; its job level and job experience the same
    ///     way, and only while the stored job is the checkpoint's; raises each active quest's progress, adding the quest
    ///     when it has no row, but never lowers it or touches a completed quest; and records when the character was last
    ///     played. One transaction.
    /// </summary>
    Task SaveCheckpointAsync(CharacterCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the drop to the character's inventory in one transaction with its ledger row (Persistence §5): the
    ///     character row is locked, capacity and stack limit are checked again, the item merges into its row, the
    ///     inventory revision goes up by one, and the ledger records the drop's ID. A drop already in the ledger
    ///     changes nothing and reports who has it.
    /// </summary>
    Task<InventoryResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken);

    /// <summary>
    ///     Wears the row in its slot in one transaction with its ledger row (Persistence §5): under the character's
    ///     lock, a repeat of the operation ID changes nothing and answers from the ledger; a row the slot held comes
    ///     out of it, a swap; the inventory revision goes up by one. <see cref="InventoryStatus.Refused" /> when the
    ///     row is not the character's or already in the slot.
    /// </summary>
    Task<InventoryResult> CommitEquipAsync(EquipCommit equip, CancellationToken cancellationToken);

    /// <summary>
    ///     Empties the slot in one transaction with its ledger row, like <see cref="CommitEquipAsync" />;
    ///     <see cref="InventoryStatus.Refused" /> when the slot is already empty.
    /// </summary>
    Task<InventoryResult> CommitUnequipAsync(UnequipCommit unequip, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes one unit of the row in one transaction with a <c>consume</c> ledger row of −1, like
    ///     <see cref="CommitEquipAsync" />; the last unit deletes the row, which then reports a quantity of 0.
    ///     <see cref="InventoryStatus.Refused" /> when the row is not the character's.
    /// </summary>
    Task<InventoryResult> CommitConsumeAsync(ConsumeCommit consume, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the quantity of the item to the character's inventory and takes its cost from the coins in one
    ///     transaction with a <c>buy</c> ledger row, like <see cref="CommitEquipAsync" />; the item merges into its row
    ///     unless its stack limit is 1. <see cref="InventoryStatus.Refused" /> when the coins are short, and
    ///     <see cref="InventoryStatus.InventoryFull" /> when the stack limit or the row limit would be passed.
    /// </summary>
    Task<InventoryResult> CommitBuyAsync(BuyCommit buy, CancellationToken cancellationToken);

    /// <summary>
    ///     Takes the quantity from the character's row and adds what it fetches to the coins in one transaction with a
    ///     <c>sell</c> ledger row, like <see cref="CommitEquipAsync" />; a row sold whole is deleted, and then reports a
    ///     quantity of 0. <see cref="InventoryStatus.Refused" /> when the row is not the character's, is worn, holds
    ///     less, or the coins would pass their cap.
    /// </summary>
    Task<InventoryResult> CommitSellAsync(SellCommit sell, CancellationToken cancellationToken);

    /// <summary>
    ///     Completes the quest and pays its reward in one transaction with a <c>quest_reward</c> ledger row, like
    ///     <see cref="CommitEquipAsync" />: the coins go up, the carried level and experience replace the stored ones
    ///     unless they would go down, the carried job pair likewise while the stored job is the reward's, and the quest's
    ///     row, added when a failed checkpoint left it missing, becomes completed. <see cref="InventoryStatus.Refused" />
    ///     when the quest is completed already, the carried progress falls short of the count, or the coins would pass
    ///     their cap. The answer carries no row.
    /// </summary>
    Task<InventoryResult> CommitQuestRewardAsync(QuestRewardCommit reward, CancellationToken cancellationToken);

    /// <summary>
    ///     Changes the character's job in one transaction (Persistence §5): under the character's lock, the stored job
    ///     becomes <see cref="JobChangeCommit.ToJob" /> at job level 1 with job experience 0, and an item worn in the
    ///     commit's slot comes out of it with an <c>unequip</c> ledger row under the operation ID and the inventory
    ///     revision up by one; a change that takes nothing off keeps no ledger row. A stored job that is already the new
    ///     one changes nothing and answers <see cref="InventoryStatus.Committed" />, from the ledger when the change took
    ///     an item off; any stored job but <see cref="JobChangeCommit.FromJob" /> answers
    ///     <see cref="InventoryStatus.Refused" />.
    /// </summary>
    Task<InventoryResult> CommitJobChangeAsync(JobChangeCommit change, CancellationToken cancellationToken);

    /// <summary>
    ///     What became of a job change whose answer was lost, settled from the stored job under the character's lock:
    ///     null when the character's job is not <paramref name="job" />, else <see cref="InventoryStatus.Committed" />
    ///     with the row the change took off, when the ledger holds one under <paramref name="operationId" />.
    /// </summary>
    Task<InventoryResult?> FindJobChangeAsync(
        Guid operationId,
        long characterId,
        string job,
        CancellationToken cancellationToken);

    /// <summary>
    ///     What became of an inventory operation whose commit may or may not have happened, looked up under the
    ///     character's lock: null when the ledger has no entry for <paramref name="operationId" />, else
    ///     <see cref="InventoryStatus.TakenByOther" /> when another character made it, or
    ///     <see cref="InventoryStatus.Committed" /> with the ledger's row and <paramref name="rowIds" /> as they are
    ///     now. The caller names the rows the ledger does not, such as the one a swap took out of its slot.
    /// </summary>
    Task<InventoryResult?> FindOperationAsync(
        Guid operationId,
        long characterId,
        IReadOnlyCollection<long> rowIds,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Every distinct job, map, item, and quest definition ID stored for any character, for the startup comparison
    ///     with the loaded content (Persistence §8).
    /// </summary>
    Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken);
}
}
