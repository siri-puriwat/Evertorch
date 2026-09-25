using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     What the server knows about one connection. It holds no credential: the token is checked once and dropped.
/// </summary>
public sealed class ClientSession
{
    public ClientSession(ConnectionId connection, uint connectedAtTick)
    {
        Connection = connection;
        ConnectedAtTick = connectedAtTick;
    }

    public ConnectionId Connection { get; }

    public uint ConnectedAtTick { get; }

    /// <summary>
    ///     This connection's command buckets; null with the abuse limits off.
    /// </summary>
    public SessionCommandLimits? CommandLimits { get; set; }

    /// <summary>
    ///     Commands refused or dropped because their class had no token left.
    /// </summary>
    public long ThrottledCommands { get; set; }

    /// <summary>
    ///     This connection's violation score; null with the abuse limits off.
    /// </summary>
    public ViolationScore? Violations { get; set; }

    /// <summary>
    ///     This connection's share of the audit log, created with its first audit event.
    /// </summary>
    public RateLimitedLog? AuditLimit { get; set; }

    public SessionState State { get; set; }

    /// <summary>
    ///     The account the session token named, once authentication succeeded. The token itself is never kept.
    /// </summary>
    public AccountId? Account { get; set; }

    /// <summary>
    ///     The account's characters as last sent in <c>CharacterList</c>; null until the first list arrives.
    ///     <c>EnterWorldRequest</c> is accepted only for one of them.
    /// </summary>
    public IReadOnlyList<CharacterSummary>? Characters { get; set; }

    /// <summary>
    ///     A <c>CreateCharacter</c> is waiting for the database; another is ignored until it answers.
    /// </summary>
    public bool IsCreatingCharacter { get; set; }

    /// <summary>
    ///     The character this connection controls in the world, if any.
    /// </summary>
    public CharacterSession? Character { get; set; }

    /// <summary>
    ///     The character this connection is loading from the database, or default.
    /// </summary>
    public CharacterId LoadingCharacter { get; set; }

    public MapInstance? Map => Character?.Map;

    public PlayerEntity? Player => Character?.Player;

    /// <summary>
    ///     Present once the session is in the world.
    /// </summary>
    public PlayerInputState? Input { get; set; }

    /// <summary>
    ///     Entities this client has been told exist. Visibility changes are sent as the difference from this set.
    /// </summary>
    public HashSet<EntityId> KnownEntities { get; } = new();

    /// <summary>
    ///     The client is owed a whole <c>InventorySnapshot</c>: it entered, attached, or asked for a resynchronization.
    /// </summary>
    public bool NeedsInventorySnapshot { get; set; }

    /// <summary>
    ///     The client is owed its character's <c>SkillList</c>: it entered or attached, or one of its casts resolved.
    /// </summary>
    public bool NeedsSkillList { get; set; }

    /// <summary>
    ///     The owner is to hear of its character's status effects in this tick's finalize phase.
    /// </summary>
    public bool NeedsStatusEffects { get; set; }

    /// <summary>
    ///     Commands that were well formed but refused: a target that is missing, hidden, or not targetable.
    /// </summary>
    public long RefusedCommands { get; set; }

    /// <summary>
    ///     The newest command sequence processed for the controlled character; 0 before the first command or without
    ///     a character. It belongs to the character, not to the connection (Network Protocol §8).
    /// </summary>
    public uint LastCommandSequence
    {
        get => Character?.LastCommandSequence ?? 0;
        set
        {
            if (Character != null)
            {
                Character.LastCommandSequence = value;
            }
        }
    }

    /// <summary>
    ///     Whether this client may be told about <paramref name="entity" />: its own entity, or one it has been sent a
    ///     spawn for. Every event is routed through this so a client never hears of an entity before its spawn.
    /// </summary>
    public bool Knows(EntityId entity)
    {
        return (Player != null && Player.Id == entity) || KnownEntities.Contains(entity);
    }
}
}
