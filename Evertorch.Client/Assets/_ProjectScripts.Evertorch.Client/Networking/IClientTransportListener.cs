using System;
using Evertorch.Protocol;

namespace Evertorch.Client
{
public interface IClientTransportListener
{
    void OnConnected();

    /// <param name="notice">The bytes the server attached to its disconnect, or empty.</param>
    void OnDisconnected(TransportDisconnectCause cause, ReadOnlySpan<byte> notice);

    void OnPayload(ProtocolChannel channel, ReadOnlySpan<byte> payload);
}
}
