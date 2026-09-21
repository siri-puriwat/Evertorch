using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
/// What the server knows about one connection. It holds no credential: the token is checked once and dropped.
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

    public MapInstance? Map { get; set; }

    public PlayerEntity? Player { get; set; }

    /// <summary>
    /// Entities this client has been told exist. Visibility changes are sent as the difference from this set.
    /// </summary>
    public HashSet<EntityId> KnownEntities { get; } = new HashSet<EntityId>();
}
}
