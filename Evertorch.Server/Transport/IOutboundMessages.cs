using System;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     What the simulation may ask of a transport. Calls come from the tick thread and must only hand work over:
///     an implementation never blocks on the network.
/// </summary>
public interface IOutboundMessages
{
    /// <summary>
    ///     Sends one encoded message on the channel and delivery its opcode is routed to. Unknown connections are
    ///     ignored, because a peer may vanish between a tick reading its input and answering it.
    /// </summary>
    void Send(ConnectionId connection, ReadOnlySpan<byte> payload);

    /// <summary>
    ///     Delivers a <see cref="DisconnectNotice" /> when possible and then closes the connection.
    /// </summary>
    void Disconnect(ConnectionId connection, DisconnectReason reason, string message);

    /// <summary>
    ///     Refuses connection requests from the connection's remote address for <c>Abuse:KickCooldownMs</c> (Network
    ///     Protocol §11). Called before <see cref="Disconnect" />, while the connection is still known; unknown
    ///     connections are ignored.
    /// </summary>
    void CoolDownAddress(ConnectionId connection);
}
}
