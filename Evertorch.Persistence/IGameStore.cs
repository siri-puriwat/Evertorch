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
    Task<PickupResult> CommitPickupAsync(PickupCommit pickup, CancellationToken cancellationToken);

    /// <summary>
    ///     What became of a drop whose commit may or may not have happened: null when the ledger has no entry for it,
    ///     else <see cref="PickupStatus.Committed" /> with the inventory as it is now when
    ///     <paramref name="characterId" /> holds it, or <see cref="PickupStatus.TakenByOther" />.
    /// </summary>
    Task<PickupResult?> FindPickupAsync(Guid dropId, long characterId, CancellationToken cancellationToken);

    /// <summary>
    ///     Every distinct job, map, and item definition ID stored for any character, for the startup comparison with
    ///     the loaded content (Persistence §8).
    /// </summary>
    Task<IReadOnlyList<string>> ListStoredDefinitionIdsAsync(CancellationToken cancellationToken);
}
}
