using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     One character in the world, and everything about it that outlives a single connection (Network Protocol §3,
///     Persistence §7): its entity, its account, its inventory, its command sequence, and its checkpoint schedule. At
///     most one exists per character.
/// </summary>
public sealed class CharacterSession
{
    public CharacterSession(
        CharacterId character,
        AccountId account,
        PlayerEntity player,
        MapInstance map,
        CharacterInventory inventory)
    {
        Character = character;
        Account = account;
        Player = player;
        Map = map;
        Inventory = inventory;
    }

    public CharacterId Character { get; }

    public AccountId Account { get; }

    public PlayerEntity Player { get; }

    public MapInstance Map { get; }

    public CharacterInventory Inventory { get; }

    /// <summary>
    ///     The connection controlling the character, or null while none does.
    /// </summary>
    public ClientSession? Connection { get; set; }

    /// <summary>
    ///     The newest command sequence processed for the character; 0 before the first command (Network Protocol §8).
    /// </summary>
    public uint LastCommandSequence { get; set; }

    /// <summary>
    ///     While no connection controls the character: the first tick on which its reconnect grace period is over.
    /// </summary>
    public uint? GraceEndsTick { get; set; }

    /// <summary>
    ///     The first tick on which the next interval checkpoint is due.
    /// </summary>
    public uint NextCheckpointTick { get; set; }

    /// <summary>
    ///     A <c>Logout</c> was accepted: no new command is taken, and the logout checkpoint is being written.
    /// </summary>
    public bool IsLoggingOut { get; set; }

    /// <summary>
    ///     The checkpoint whose completion finishes the logout; a completion of any other checkpoint does not.
    /// </summary>
    public PersistenceJob? LogoutCheckpoint { get; set; }

    /// <summary>
    ///     The sequence of the accepted <c>Logout</c>, answered if its checkpoint fails.
    /// </summary>
    public uint LogoutSequence { get; set; }

    /// <summary>
    ///     The pickup being committed, if any. Logout and removal wait for it (Persistence §7).
    /// </summary>
    public PendingPickup? Pickup { get; set; }

    /// <summary>
    ///     The character was to leave the world while a pickup was in flight; it leaves once the pickup settles.
    /// </summary>
    public bool IsRemovalDeferred { get; set; }
}
}
