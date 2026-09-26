using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     One character in the world, and everything about it that outlives a single connection (Network Protocol §3,
///     Persistence §7): its entity, its account, its inventory, its quests, its command sequence, and its checkpoint
///     schedule. At most one exists per character.
/// </summary>
public sealed class CharacterSession
{
    public CharacterSession(
        CharacterId character,
        AccountId account,
        PlayerEntity player,
        MapInstance map,
        CharacterInventory inventory,
        CharacterQuests? quests = null)
    {
        Character = character;
        Account = account;
        Player = player;
        Map = map;
        Inventory = inventory;
        Quests = quests ?? new CharacterQuests();
    }

    public CharacterId Character { get; }

    public AccountId Account { get; }

    public PlayerEntity Player { get; }

    /// <summary>
    ///     The map instance the character is on; a map transfer moves it (Gameplay Systems §4.2).
    /// </summary>
    public MapInstance Map { get; set; }

    /// <summary>
    ///     The character stands in a portal to a map this server has not loaded, which was logged once.
    /// </summary>
    public bool IsInDeadEndPortal { get; set; }

    /// <summary>
    ///     The tick it last arrived through a portal; null before its first crossing.
    /// </summary>
    public uint? ArrivedThroughPortalTick { get; set; }

    public CharacterInventory Inventory { get; }

    public CharacterQuests Quests { get; }

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
    ///     Its connection was closed for violations: the character is checkpointed and removed, once an inventory
    ///     operation in flight settles, and no connection may attach to it (Network Protocol §3).
    /// </summary>
    public bool IsExpelled { get; set; }

    /// <summary>
    ///     The checkpoint whose completion finishes the logout; a completion of any other checkpoint does not.
    /// </summary>
    public PersistenceJob? LogoutCheckpoint { get; set; }

    /// <summary>
    ///     The sequence of the accepted <c>Logout</c>, answered if its checkpoint fails.
    /// </summary>
    public uint LogoutSequence { get; set; }

    /// <summary>
    ///     The inventory operation being committed, if any: one at a time, so the inventory changes in commit order.
    ///     Logout, removal, and a map transfer wait for it (Persistence §7).
    /// </summary>
    public InventoryOperation? Operation { get; set; }

    /// <summary>
    ///     The character was to leave the world while an inventory operation was in flight; it leaves once the
    ///     operation settles.
    /// </summary>
    public bool IsRemovalDeferred { get; set; }
}
}
