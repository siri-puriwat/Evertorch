using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DisconnectReason = Evertorch.Protocol.DisconnectReason;

namespace Evertorch.Server
{
/// <summary>
///     The only place the server touches LiteNetLib. Library callbacks run on its own threads; all they do is decode
///     into the <see cref="InboundQueue" /> and keep the peer table, so the simulation never sees a library type.
/// </summary>
public sealed class LiteNetLibServerTransport : IServerTransport, INetEventListener, IDisposable
{
    private static readonly Action<ILogger, string, int, Exception?> LogListening =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(3001, "TransportListening"),
            "Listening for clients on {Address}:{Port}.");

    private static readonly Action<ILogger, SocketError, Exception?> LogSocketError =
        LoggerMessage.Define<SocketError>(
            LogLevel.Warning,
            new EventId(3002, "TransportSocketError"),
            "The transport reported socket error {SocketError}.");

    private readonly InboundQueue m_inbound;
    private readonly NetworkOptions m_options;
    private readonly ILogger<LiteNetLibServerTransport> m_logger;
    private readonly NetManager m_manager;

    private readonly ConcurrentDictionary<ConnectionId, NetPeer> m_peers = new();

    private long m_lastConnection;
    private volatile bool m_isAdmissionClosed;

    public LiteNetLibServerTransport(
        InboundQueue inbound,
        IOptions<NetworkOptions> options,
        ILogger<LiteNetLibServerTransport> logger)
    {
        m_inbound = inbound;
        m_options = options.Value;
        m_logger = logger;
        m_manager = new NetManager(this)
        {
            ChannelsCount = MessageRouting.ChannelCount,
            AutoRecycle = true,
            IPv6Enabled = false,
            EnableStatistics = true,
            DisconnectTimeout = m_options.DisconnectTimeoutMs,

            // No protocol message needs more than one datagram. Left at the library's default, a peer that knows
            // only the connection key could announce huge fragmented messages and make the server reserve memory
            // for them before a single byte is checked. The same limit applies to sends, so a reliable message
            // that outgrows one datagram fails loudly instead of fragmenting.
            MaxFragmentsCount = 1,

            // Callbacks fire straight from the library's threads instead of waiting for a poll, which would add up
            // to a timer quantum of latency to every input.
            UnsyncedEvents = true
        };
    }

    public void Dispose()
    {
        if (m_manager.IsRunning)
        {
            m_manager.Stop(false);
        }
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        if (m_isAdmissionClosed)
        {
            request.Reject(EncodeNotice(DisconnectReason.Maintenance, string.Empty));
            return;
        }

        if (m_manager.ConnectedPeersCount >= m_options.MaxConnections)
        {
            request.Reject(EncodeNotice(DisconnectReason.ServerFull, string.Empty));
            return;
        }

        request.AcceptIfKey(m_options.ConnectionKey);
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        var connection = new ConnectionId(Interlocked.Increment(ref m_lastConnection));
        peer.Tag = new PeerState(connection);
        m_peers[connection] = peer;
        m_inbound.OnConnected(connection);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (!(peer.Tag is PeerState state))
        {
            return;
        }

        m_peers.TryRemove(state.Connection, out NetPeer? _);

        // The session layer already removed a connection it closed itself; telling it again would only be noise.
        if (!state.IsClosedByServer)
        {
            m_inbound.OnDisconnected(state.Connection);
        }
    }

    void INetEventListener.OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        if (peer.Tag is PeerState state)
        {
            m_inbound.OnPayload(state.Connection, (ProtocolChannel)channelNumber, reader.GetRemainingBytesSpan());
        }
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        LogSocketError(m_logger, socketError, null);
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

    void INetEventListener.OnMessageDelivered(NetPeer peer, object userData)
    {
    }

    void INetEventListener.OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
    {
    }

    public int LocalPort => m_manager.LocalPort;

    public void Start()
    {
        var address = IPAddress.Parse(m_options.BindAddress);
        if (!m_manager.Start(address, IPAddress.IPv6Any, m_options.Port))
        {
            throw new InvalidOperationException(
                $"The transport could not bind {m_options.BindAddress}:{m_options.Port}.");
        }

        LogListening(m_logger, m_options.BindAddress, m_manager.LocalPort, null);
    }

    public void CloseAdmission()
    {
        m_isAdmissionClosed = true;
    }

    public void Stop()
    {
        CloseAdmission();
        byte[] notice = EncodeNotice(DisconnectReason.Maintenance, string.Empty);
        foreach (KeyValuePair<ConnectionId, NetPeer> entry in m_peers)
        {
            MarkClosedByServer(entry.Value);
            m_manager.DisconnectPeer(entry.Value, notice);
        }

        m_peers.Clear();
        m_manager.Stop(true);
    }

    public void Send(ConnectionId connection, ReadOnlySpan<byte> payload)
    {
        if (!m_peers.TryGetValue(connection, out NetPeer? peer))
        {
            return;
        }

        if (!MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode)
            || !MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery delivery))
        {
            throw new ArgumentException("The payload does not start with a routable opcode.", nameof(payload));
        }

        DeliveryMethod method = delivery == MessageDelivery.ReliableOrdered
            ? DeliveryMethod.ReliableOrdered
            : DeliveryMethod.Sequenced;
        peer.Send(payload, (byte)channel, method);
    }

    public void Disconnect(ConnectionId connection, DisconnectReason reason, string message)
    {
        if (!m_peers.TryRemove(connection, out NetPeer? peer))
        {
            return;
        }

        // The notice rides in the disconnect packet itself, which the library retransmits, so it needs no channel.
        MarkClosedByServer(peer);
        m_manager.DisconnectPeer(peer, EncodeNotice(reason, message));
    }

    public TransportStatistics GetStatistics()
    {
        NetStatistics statistics = m_manager.Statistics;
        return new TransportStatistics(
            statistics.BytesReceived,
            statistics.BytesSent,
            statistics.PacketsReceived,
            statistics.PacketsSent,
            statistics.PacketLoss);
    }

    public bool TryGetRoundTripTime(ConnectionId connection, out int milliseconds)
    {
        if (m_peers.TryGetValue(connection, out NetPeer? peer))
        {
            milliseconds = peer.RoundTripTime;
            return true;
        }

        milliseconds = 0;
        return false;
    }

    private static void MarkClosedByServer(NetPeer peer)
    {
        if (peer.Tag is PeerState state)
        {
            state.IsClosedByServer = true;
        }
    }

    private static byte[] EncodeNotice(DisconnectReason reason, string message)
    {
        var notice = new DisconnectNotice(reason, message);
        byte[] payload = new byte[notice.GetEncodedLength()];
        notice.Write(payload);
        return payload;
    }

    private sealed class PeerState
    {
        private volatile bool m_isClosedByServer;

        public PeerState(ConnectionId connection)
        {
            Connection = connection;
        }

        public ConnectionId Connection { get; }

        public bool IsClosedByServer
        {
            get => m_isClosedByServer;
            set => m_isClosedByServer = value;
        }
    }
}
}
