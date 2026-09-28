using System;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's view of a network library. Everything above it deals in protocol channels and payload bytes only.
///     Disposing it releases its socket, which may outlive play mode when the editor keeps the domain loaded.
/// </summary>
public interface IClientTransport : IDisposable
{
    bool IsConnected { get; }

    int RoundTripMilliseconds { get; }

    void Connect(string host, int port);

    void Disconnect();

    void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload);

    /// <summary>
    ///     Delivers everything that arrived since the last call, on the calling thread.
    /// </summary>
    void Poll(IClientTransportListener listener);
}
}
