using System;
using System.Net;
using System.Net.Sockets;
using Evertorch.Protocol;
using LiteNetLib;
using DisconnectReason = LiteNetLib.DisconnectReason;

namespace Evertorch.Client
{
/// <summary>
///     The only client code that knows LiteNetLib. Events are raised from <see cref="Poll" />, so everything above it
///     runs on Unity's main thread.
/// </summary>
public sealed class LiteNetLibClientTransport : IClientTransport, IDisposable
{
    private readonly NetManager m_manager;
    private readonly string m_connectionKey;
    private NetPeer? m_peer;
    private IClientTransportListener? m_listener;

    public LiteNetLibClientTransport(string connectionKey, int disconnectTimeoutMilliseconds)
    {
        m_connectionKey = connectionKey ?? throw new ArgumentNullException(nameof(connectionKey));
        m_manager = new NetManager(new EventListener(this))
        {
            ChannelsCount = MessageRouting.ChannelCount,
            AutoRecycle = true,
            IPv6Enabled = false,
            UnsyncedEvents = false,
            DisconnectTimeout = disconnectTimeoutMilliseconds,

            // The protocol never fragments, so a peer announcing a fragmented message is not speaking it.
            MaxFragmentsCount = 1
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

    private void OnPeerConnected()
    {
        m_listener?.OnConnected();
    }

    private void OnPeerDisconnected(DisconnectInfo disconnectInfo)
    {
        m_peer = null;
        NetPacketReader? data = disconnectInfo.AdditionalData;
        bool hasNotice = data != null && data.AvailableBytes > 0;
        ReadOnlySpan<byte> notice = hasNotice ? data!.GetRemainingBytesSpan() : ReadOnlySpan<byte>.Empty;
        m_listener?.OnDisconnected(ToCause(disconnectInfo.Reason), notice);
    }

    private void OnNetworkReceive(NetPacketReader reader, byte channelNumber)
    {
        m_listener?.OnPayload((ProtocolChannel)channelNumber, reader.GetRemainingBytesSpan());
    }

    private static TransportDisconnectCause ToCause(DisconnectReason reason)
    {
        switch (reason)
        {
            case DisconnectReason.DisconnectPeerCalled:
                return TransportDisconnectCause.ClosedLocally;
            case DisconnectReason.RemoteConnectionClose:
            case DisconnectReason.ConnectionRejected:
                return TransportDisconnectCause.ClosedByServer;
            case DisconnectReason.Timeout:
                return TransportDisconnectCause.TimedOut;
            case DisconnectReason.ConnectionFailed:
            case DisconnectReason.HostUnreachable:
            case DisconnectReason.NetworkUnreachable:
            case DisconnectReason.UnknownHost:
                return TransportDisconnectCause.ConnectionFailed;
            default:
                return TransportDisconnectCause.Other;
        }
    }

    // Kept off the public type so that assemblies using the transport need no reference to the network library.
    private sealed class EventListener : INetEventListener
    {
        private readonly LiteNetLibClientTransport m_owner;

        public EventListener(LiteNetLibClientTransport owner)
        {
            m_owner = owner;
        }

        public void OnPeerConnected(NetPeer peer)
        {
            m_owner.OnPeerConnected();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            m_owner.OnPeerDisconnected(disconnectInfo);
        }

        public void OnNetworkReceive(
            NetPeer peer,
            NetPacketReader reader,
            byte channelNumber,
            DeliveryMethod deliveryMethod)
        {
            m_owner.OnNetworkReceive(reader, channelNumber);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
        }

        public void OnNetworkReceiveUnconnected(
            IPEndPoint remoteEndPoint,
            NetPacketReader reader,
            UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            request.Reject();
        }
    }
}
}
