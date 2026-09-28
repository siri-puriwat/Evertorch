using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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
    private readonly AddressThrottle m_throttle;
    private readonly ServerInstruments m_instruments;
    private readonly AuditLog m_audit;
    private readonly ILogger<LiteNetLibServerTransport> m_logger;
    private readonly NetManager m_manager;
    private readonly ConnectionRegistry m_connections;

    private readonly ConcurrentDictionary<ConnectionId, NetPeer> m_peers = new();

    // The session layer judges what a connection sent only when it drains the queue, so it may ask for a cooldown
    // after the peer has left on its own. Their addresses are kept for that, the oldest forgotten first.
    private readonly ConcurrentDictionary<ConnectionId, IPAddress> m_departed = new();
    private readonly ConcurrentQueue<ConnectionId> m_departures = new();

    // An accepted request's ID, until its peer's connected callback, which the library can raise inside Accept.
    private readonly ConcurrentDictionary<IPEndPoint, ConnectionId> m_admitted = new();

    private volatile bool m_isAdmissionClosed;

    public LiteNetLibServerTransport(
        InboundQueue inbound,
        IOptions<NetworkOptions> options,
        AddressThrottle throttle,
        ServerInstruments instruments,
        AuditLog audit,
        ConnectionRegistry connections,
        ILogger<LiteNetLibServerTransport> logger)
    {
        m_inbound = inbound;
        m_connections = connections;
        m_options = options.Value;
        m_throttle = throttle;
        m_instruments = instruments;
        m_audit = audit;
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

    /// <summary>
    ///     The UDP port actually bound, which differs from the configured one when that is 0.
    /// </summary>
    public int LocalPort => m_manager.LocalPort;

    public void Dispose()
    {
        if (m_manager.IsRunning)
        {
            m_manager.Stop(false);
        }
    }

    // Throttling comes first and answers nothing at all: a forced reject creates no peer and sends no packet. Then the
    // key, so a requester without it is rejected without data and learns nothing about the server's state. Only then
    // the cooldown, admission, and capacity, each told with a notice (Network Protocol §7).
    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        IPAddress address = request.RemoteEndPoint.Address;
        if (!m_throttle.TryAdmit(address, out string limit))
        {
            Refused(limit);
            request.RejectForce();
            return;
        }

        if (!HasConnectionKey(request))
        {
            request.Reject();
            return;
        }

        if (m_throttle.IsCoolingDown(address))
        {
            Refused(ServerInstruments.AddressCooldownLimit);
            request.Reject(EncodeNotice(DisconnectReason.RateLimited, string.Empty));
            return;
        }

        if (m_isAdmissionClosed)
        {
            request.Reject(EncodeNotice(DisconnectReason.Maintenance, string.Empty));
            return;
        }

        // The slot is the registry's from here until this peer is reported gone, across every transport.
        if (!m_connections.TryAdmit(this, out ConnectionId connection))
        {
            request.Reject(EncodeNotice(DisconnectReason.ServerFull, string.Empty));
            return;
        }

        m_admitted[request.RemoteEndPoint] = connection;
        if (request.Accept() == null && m_admitted.TryRemove(request.RemoteEndPoint, out ConnectionId _))
        {
            m_connections.Release(connection);
        }
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        if (!m_admitted.TryRemove(new IPEndPoint(peer.Address, peer.Port), out ConnectionId connection))
        {
            return;
        }

        peer.Tag = new PeerState(connection);
        m_peers[connection] = peer;
        m_throttle.OnConnected(peer.Address);
        m_inbound.OnConnected(connection);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (!(peer.Tag is PeerState state))
        {
            return;
        }

        // Before the peer leaves the table, so a cooldown asked for meanwhile finds its address in one or the other.
        if (!state.IsClosedByServer)
        {
            RememberDeparture(state.Connection, peer.Address);
        }

        m_peers.TryRemove(state.Connection, out NetPeer? _);
        m_throttle.OnDisconnected(peer.Address);

        // The session layer already removed a connection it closed itself; only the queue's budget is left to forget.
        if (state.IsClosedByServer)
        {
            m_inbound.OnClosed(state.Connection);
        }
        else
        {
            m_inbound.OnDisconnected(state.Connection);
        }

        m_connections.Release(state.Connection);
    }

    void INetEventListener.OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        // A peer the server closed lingers in the library, and may go on sending, until it confirms the close. It has
        // no session and no budget left, so nothing it sends may reach the queue.
        if (peer.Tag is PeerState state && !state.IsClosedByServer)
        {
            // Only the two methods the protocol uses are named; any other makes the message malformed.
            MessageDelivery? delivery = deliveryMethod switch
            {
                DeliveryMethod.ReliableOrdered => MessageDelivery.ReliableOrdered,
                DeliveryMethod.Sequenced => MessageDelivery.UnreliableSequenced,
                _ => null
            };
            m_inbound.OnPayload(
                state.Connection,
                (ProtocolChannel)channelNumber,
                delivery,
                reader.GetRemainingBytesSpan());
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
        if (peer.Tag is PeerState state)
        {
            state.HasRoundTripTime = true;
        }
    }

    void INetEventListener.OnMessageDelivered(NetPeer peer, object userData)
    {
    }

    void INetEventListener.OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
    {
    }

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

    public bool IsAdmissionOpen => !m_isAdmissionClosed;

    public void CloseAdmission()
    {
        m_isAdmissionClosed = true;
    }

    public void Stop(DisconnectReason reason, string message)
    {
        CloseAdmission();
        byte[] notice = EncodeNotice(reason, message);
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

    public void CoolDownAddress(ConnectionId connection)
    {
        if (m_peers.TryGetValue(connection, out NetPeer? peer))
        {
            m_throttle.StartCooldown(peer.Address);
        }
        else if (m_departed.TryRemove(connection, out IPAddress? address))
        {
            m_throttle.StartCooldown(address);
        }
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
        // The library reports a round trip of 0 before it has measured one.
        if (m_peers.TryGetValue(connection, out NetPeer? peer) && peer.Tag is PeerState { HasRoundTripTime: true })
        {
            milliseconds = peer.RoundTripTime;
            return true;
        }

        milliseconds = 0;
        return false;
    }

    private void Refused(string limit)
    {
        m_instruments.RecordRateLimited(limit);
        m_audit.ConnectionRefused(default, null, limit);
    }

    private void RememberDeparture(ConnectionId connection, IPAddress address)
    {
        m_departed[connection] = address;
        m_departures.Enqueue(connection);
        while (m_departures.Count > m_options.MaxConnections && m_departures.TryDequeue(out ConnectionId oldest))
        {
            m_departed.TryRemove(oldest, out IPAddress? _);
        }
    }

    private static void MarkClosedByServer(NetPeer peer)
    {
        if (peer.Tag is PeerState state)
        {
            state.IsClosedByServer = true;
        }
    }

    private bool HasConnectionKey(ConnectionRequest request)
    {
        try
        {
            return request.Data.TryGetString(out string key)
                && string.Equals(key, m_options.ConnectionKey, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // Not valid UTF-8, so not the key.
            return false;
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
        private volatile bool m_hasRoundTripTime;

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

        public bool HasRoundTripTime
        {
            get => m_hasRoundTripTime;
            set => m_hasRoundTripTime = value;
        }
    }
}
}
