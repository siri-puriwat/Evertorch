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
    ///     The character with its inventory, or null when the account owns no such character.
    /// </summary>
    Task<StoredCharacter?> LoadCharacterAsync(AccountId account, long characterId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the checkpoint over the character's map, position, and HP, and records when it was last played.
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
    ///     Every distinct job, map, and item definition ID stored for any character, for the startup comparison with
    ///     the loaded content (Persistence §8).
    /// </summary>
    Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken);
}
}
