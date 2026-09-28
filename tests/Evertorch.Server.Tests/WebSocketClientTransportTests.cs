using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client's WebSocket transport (Network Protocol §7) over a scripted connection: the URL, the framing, the
///     heartbeat both ways, the silence limit, and what each kind of close tells the listener.
/// </summary>
[TestFixture]
public sealed class WebSocketClientTransportTests
{
    [SetUp]
    public void Compose()
    {
        m_connections.Clear();
        m_now = 0;
        m_transport = new WebSocketClientTransport(
            () =>
            {
                var connection = new FakeConnection();
                m_connections.Add(connection);
                return connection;
            },
            "evertorch",
            TimeoutMs,
            () => m_now);
        m_listener = new Listener();
    }

    private const int TimeoutMs = 4000;

    private readonly List<FakeConnection> m_connections = new();
    private double m_now;
    private WebSocketClientTransport m_transport = null!;
    private Listener m_listener = null!;

    private FakeConnection Connection => m_connections.Last();

    private static byte[] Heartbeat(byte kind, uint nonce)
    {
        byte[] message = new byte[WebSocketClientTransport.HeartbeatLength];
        message[0] = WebSocketClientTransport.HeartbeatChannel;
        message[1] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(2), nonce);
        return message;
    }

    private static byte[] Framed(ProtocolChannel channel, byte[] payload)
    {
        return new[] { (byte)channel }.Concat(payload).ToArray();
    }

    private static byte[] Notice()
    {
        var notice = new DisconnectNotice(DisconnectReason.Maintenance, "back soon");
        byte[] payload = new byte[notice.GetEncodedLength()];
        notice.Write(payload);
        return payload;
    }

    private static byte[] Despawn()
    {
        byte[] payload = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(payload);
        return payload;
    }

    private void Open()
    {
        m_transport.Connect("127.0.0.1", 7443);
        Connection.State = WebSocketConnectionState.Open;
        m_transport.Poll(m_listener);
    }

    [TestCase("127.0.0.1", 7443, "wss://127.0.0.1:7443/play?key=evertorch")]
    [TestCase("play.example.com", 443, "wss://play.example.com:443/play?key=evertorch")]
    [TestCase("::1", 7443, "wss://[::1]:7443/play?key=evertorch")]
    public void Connect_OpensThePlayUrlOfTheGateway(string host, int port, string url)
    {
        m_transport.Connect(host, port);

        Assert.That(Connection.Url, Is.EqualTo(url));
    }

    [TestCase(new byte[] { 3, 1, 2 }, TestName = "Receive_OnAnUnknownChannel_ClosesTheConnection")]
    [TestCase(new byte[0], TestName = "Receive_AnEmptyMessage_ClosesTheConnection")]
    [TestCase(new byte[] { 255, 1, 0 }, TestName = "Receive_AShortHeartbeat_ClosesTheConnection")]
    public void Receive_BreakingTheFraming_ClosesTheConnection(byte[] message)
    {
        Open();
        Connection.Inbox.Enqueue(message);
        Connection.Inbox.Enqueue(Framed(ProtocolChannel.Control, Despawn()));

        m_transport.Poll(m_listener);

        Assert.That(Connection.IsCloseRequested, Is.True);
        Assert.That(m_listener.Disconnects.Single().Cause, Is.EqualTo(TransportDisconnectCause.Other));
        Assert.That(m_listener.Payloads, Is.Empty, "nothing after the break is read");
    }

    private sealed class FakeConnection : IWebSocketConnection
    {
        public string Url { get; private set; } = string.Empty;

        public Queue<byte[]> Inbox { get; } = new();

        public List<byte[]> Sent { get; } = new();

        public bool IsCloseRequested { get; private set; }

        public bool IsDisposed { get; private set; }

        public WebSocketConnectionState State { get; set; } = WebSocketConnectionState.Connecting;

        public void Open(string url)
        {
            Url = url;
        }

        public void Send(byte[] message)
        {
            if (State == WebSocketConnectionState.Open)
            {
                Sent.Add(message);
            }
        }

        public bool TryReceive(out byte[] message)
        {
            return Inbox.TryDequeue(out message!);
        }

        public void Close()
        {
            IsCloseRequested = true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class Listener : IClientTransportListener
    {
        public int Connected { get; private set; }

        public List<(ProtocolChannel Channel, byte[] Payload)> Payloads { get; } = new();

        public List<(TransportDisconnectCause Cause, byte[] Notice)> Disconnects { get; } = new();

        public void OnConnected()
        {
            Connected++;
        }

        public void OnDisconnected(TransportDisconnectCause cause, ReadOnlySpan<byte> notice)
        {
            Disconnects.Add((cause, notice.ToArray()));
        }

        public void OnPayload(ProtocolChannel channel, ReadOnlySpan<byte> payload)
        {
            Payloads.Add((channel, payload.ToArray()));
        }
    }

    [Test]
    public void Close_BeforeTheSocketEverOpened_IsAConnectionThatFailed()
    {
        m_transport.Connect("127.0.0.1", 7443);
        Connection.State = WebSocketConnectionState.Failed;

        m_transport.Poll(m_listener);

        Assert.That(m_listener.Connected, Is.Zero);
        Assert.That(m_listener.Disconnects.Single().Cause, Is.EqualTo(TransportDisconnectCause.ConnectionFailed));
    }

    [Test]
    public void Close_ByTheServer_AfterItsNotice_ReportsTheNoticeWithTheClose_NotAsAPayload()
    {
        Open();
        Connection.Inbox.Enqueue(Framed(ProtocolChannel.Control, Notice()));
        m_transport.Poll(m_listener);
        Connection.State = WebSocketConnectionState.Closed;

        m_transport.Poll(m_listener);

        (TransportDisconnectCause cause, byte[] notice) = m_listener.Disconnects.Single();
        Assert.That(cause, Is.EqualTo(TransportDisconnectCause.ClosedByServer));
        Assert.That(notice, Is.EqualTo(Notice()));
        Assert.That(m_listener.Payloads, Is.Empty);
        Assert.That(Connection.IsDisposed, Is.True);
    }

    [Test]
    public void Disconnect_ByTheClient_ClosesTheSocket_AndReportsClosedLocally()
    {
        Open();
        m_transport.Disconnect();
        Connection.State = WebSocketConnectionState.Closed;

        m_transport.Poll(m_listener);

        Assert.That(Connection.IsCloseRequested, Is.True);
        Assert.That(m_listener.Disconnects.Single().Cause, Is.EqualTo(TransportDisconnectCause.ClosedLocally));
    }

    [Test]
    public void Heartbeat_FromTheServer_IsAnsweredWithItsNonce_AndReachesNoListener()
    {
        Open();
        Connection.Inbox.Enqueue(Heartbeat(WebSocketClientTransport.HeartbeatPing, 77));

        m_transport.Poll(m_listener);

        Assert.That(Connection.Sent, Is.EqualTo(new[] { Heartbeat(WebSocketClientTransport.HeartbeatPong, 77) }));
        Assert.That(m_listener.Payloads, Is.Empty);
    }

    [Test]
    public void Heartbeat_OfTheClient_GoesOutEachSecond_AndItsAnswerGivesTheRoundTrip()
    {
        Open();
        m_now = 1.0;
        m_transport.Poll(m_listener);
        byte[] ping = Connection.Sent.Single();
        uint nonce = BinaryPrimitives.ReadUInt32LittleEndian(ping.AsSpan(2));
        m_now = 1.08;
        Connection.Inbox.Enqueue(Heartbeat(WebSocketClientTransport.HeartbeatPong, nonce));

        m_transport.Poll(m_listener);

        Assert.That(ping[1], Is.EqualTo(WebSocketClientTransport.HeartbeatPing));
        Assert.That(m_transport.RoundTripMilliseconds, Is.EqualTo(80));
    }

    [Test]
    public void Poll_OnceTheSocketOpens_ReportsConnectedOnce()
    {
        Open();
        m_transport.Poll(m_listener);

        Assert.That(m_listener.Connected, Is.EqualTo(1));
        Assert.That(m_transport.IsConnected, Is.True);
    }

    [Test]
    public void Receive_AMessageAtTheLimit_IsDelivered()
    {
        Open();
        byte[] message = new byte[WebSocketClientTransport.MaxMessageBytes];
        Despawn().CopyTo(message, 1);
        Connection.Inbox.Enqueue(message);

        m_transport.Poll(m_listener);

        Assert.That(m_listener.Payloads, Has.Count.EqualTo(1));
        Assert.That(m_listener.Disconnects, Is.Empty);
    }

    [Test]
    public void Receive_AMessageLongerThanTheLimit_ClosesTheConnection()
    {
        Open();
        Connection.Inbox.Enqueue(new byte[WebSocketClientTransport.MaxMessageBytes + 1]);

        m_transport.Poll(m_listener);

        Assert.That(m_listener.Disconnects.Single().Cause, Is.EqualTo(TransportDisconnectCause.Other));
    }

    [Test]
    public void Receive_AMessage_DeliversItsPayloadOnItsChannel()
    {
        Open();
        Connection.Inbox.Enqueue(Framed(ProtocolChannel.Control, Despawn()));

        m_transport.Poll(m_listener);

        Assert.That(m_listener.Payloads.Single().Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(m_listener.Payloads.Single().Payload, Is.EqualTo(Despawn()));
    }

    [Test]
    public void Send_PutsTheChannelByteFirst_AndNothingGoesOutBeforeTheSocketOpens()
    {
        m_transport.Connect("127.0.0.1", 7443);
        m_transport.Send(ProtocolChannel.Control, MessageDelivery.ReliableOrdered, Despawn());
        Connection.State = WebSocketConnectionState.Open;
        m_transport.Poll(m_listener);

        m_transport.Send(ProtocolChannel.Input, MessageDelivery.UnreliableSequenced, Despawn());

        Assert.That(Connection.Sent, Is.EqualTo(new[] { Framed(ProtocolChannel.Input, Despawn()) }));
    }

    [Test]
    public void Silence_ForTheDisconnectTimeout_ClosesAsTimedOut()
    {
        Open();
        m_now = TimeoutMs / 1000.0;

        m_transport.Poll(m_listener);

        Assert.That(Connection.IsCloseRequested, Is.True);
        Assert.That(m_listener.Disconnects.Single().Cause, Is.EqualTo(TransportDisconnectCause.TimedOut));
        Assert.That(m_transport.IsConnected, Is.False);
    }
}
}
