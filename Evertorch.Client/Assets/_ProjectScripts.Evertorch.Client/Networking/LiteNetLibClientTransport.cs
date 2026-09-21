using System;
using System.Net;
using System.Net.Sockets;
using Evertorch.Protocol;
using LiteNetLib;

namespace Evertorch.Client
{
/// <summary>
/// The only client code that knows LiteNetLib. Events are raised from <see cref="Poll"/>, so everything above it
/// runs on Unity's main thread.
/// </summary>
public sealed class LiteNetLibClientTransport : IClientTransport, INetEventListener, IDisposable
{
    private readonly NetManager m_manager;
    private readonly string m_connectionKey;
    private NetPeer? m_peer;
    private IClientTransportListener? m_listener;

    public LiteNetLibClientTransport(string connectionKey, int disconnectTimeoutMilliseconds)
    {
        m_connectionKey = connectionKey ?? throw new ArgumentNullException(nameof(connectionKey));
        m_manager = new NetManager(this)
        {
            ChannelsCount = MessageRouting.ChannelCount,
            AutoRecycle = true,
            IPv6Enabled = false,
            UnsyncedEvents = false,
            DisconnectTimeout = disconnectTimeoutMilliseconds,
        };
    }

    public bool IsConnected => m_peer != null && m_peer.ConnectionState == ConnectionState.Connected;

    public int RoundTripMilliseconds => m_peer == null ? 0 : m_peer.RoundTripTime;

    public void Connect(string host, int port)
    {
        if (!m_manager.IsRunning && !m_manager.Start())
        {
            throw new InvalidOperationException("The client socket could not be opened.");
        }

        m_peer = m_manager.Connect(host, port, m_connectionKey);
    }

    public void Disconnect()
    {
        m_peer?.Disconnect();
    }

    public void Send(ProtocolChannel channel, MessageDelivery delivery, ReadOnlySpan<byte> payload)
    {
        if (m_peer == null || m_peer.ConnectionState != ConnectionState.Connected)
        {
            return;
        }

        DeliveryMethod method = delivery == MessageDelivery.ReliableOrdered
            ? DeliveryMethod.ReliableOrdered
            : DeliveryMethod.Sequenced;
        m_peer.Send(payload, (byte)channel, method);
    }

    public void Poll(IClientTransportListener listener)
    {
        m_listener = listener ?? throw new ArgumentNullException(nameof(listener));
        try
        {
            m_manager.PollEvents();
        }
        finally
        {
            m_listener = null;
        }
    }

    // The socket thread outlives play mode when the editor keeps the domain loaded, so stopping is not optional.
    public void Dispose()
    {
        m_peer = null;
        m_manager.Stop(true);
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        m_listener?.OnConnected();
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        m_peer = null;
        ReadOnlySpan<byte> notice = disconnectInfo.AdditionalData != null
            && disconnectInfo.AdditionalData.AvailableBytes > 0
                ? disconnectInfo.AdditionalData.GetRemainingBytesSpan()
                : ReadOnlySpan<byte>.Empty;
        m_listener?.OnDisconnected(ToCause(disconnectInfo.Reason), notice);
    }

    void INetEventListener.OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        m_listener?.OnPayload((ProtocolChannel)channelNumber, reader.GetRemainingBytesSpan());
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
    }

    void INetEventListener.OnNetworkReceiveUnconnected(
        IPEndPoint remoteEndPoint,
        NetPacketReader reader,
        UnconnectedMessageType messageType)
    {
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        request.Reject();
    }

    private static TransportDisconnectCause ToCause(LiteNetLib.DisconnectReason reason)
    {
        switch (reason)
        {
            case LiteNetLib.DisconnectReason.DisconnectPeerCalled:
                return TransportDisconnectCause.ClosedLocally;
            case LiteNetLib.DisconnectReason.RemoteConnectionClose:
            case LiteNetLib.DisconnectReason.ConnectionRejected:
                return TransportDisconnectCause.ClosedByServer;
            case LiteNetLib.DisconnectReason.Timeout:
                return TransportDisconnectCause.TimedOut;
            case LiteNetLib.DisconnectReason.ConnectionFailed:
            case LiteNetLib.DisconnectReason.HostUnreachable:
            case LiteNetLib.DisconnectReason.NetworkUnreachable:
            case LiteNetLib.DisconnectReason.UnknownHost:
                return TransportDisconnectCause.ConnectionFailed;
            default:
                return TransportDisconnectCause.Other;
        }
    }
}
}
