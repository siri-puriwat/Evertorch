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

    public MapInstance? Map { get; set; }

    public PlayerEntity? Player { get; set; }

    /// <summary>
    ///     Present once the session is in the world.
    /// </summary>
    public PlayerInputState? Input { get; set; }

    /// <summary>
    ///     Entities this client has been told exist. Visibility changes are sent as the difference from this set.
    /// </summary>
    public HashSet<EntityId> KnownEntities { get; } = new();

    /// <summary>
    ///     Commands that were well formed but refused: a target that is missing, hidden, or not targetable.
    /// </summary>
    public long RefusedCommands { get; set; }

    /// <summary>
    ///     The newest command sequence processed for this session; 0 before the first command.
    /// </summary>
    public uint LastCommandSequence { get; set; }

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
