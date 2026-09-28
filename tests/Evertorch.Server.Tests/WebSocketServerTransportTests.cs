using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The WebSocket adapter (Network Protocol §7), mirroring <see cref="LiteNetLibServerTransportTests" /> where a
///     case applies, over loopback TCP pairs: admission and its notices, framing and its limits, the heartbeat, the
///     bounded send queue, and the stop.
/// </summary>
[TestFixture]
public sealed class WebSocketServerTransportTests
{
    private const string Key = "evertorch";

    private static byte[] Hello()
    {
        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, 1, "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static byte[] Despawn()
    {
        byte[] payload = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(payload);
        return payload;
    }

    // What the transport puts before a payload: the channel its opcode is routed on.
    private static byte[] Framed(byte[] payload)
    {
        MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode);
        MessageRouting.TryGetRoute(opcode, out ProtocolChannel channel, out MessageDelivery _);
        return TestWebSocketPeer.Frame((byte)channel, payload);
    }

    private static DisconnectNotice ReadNotice(byte[]? message)
    {
        Assert.That(message, Is.Not.Null, "a message arrived");
        Assert.That(message![0], Is.EqualTo((byte)ProtocolChannel.Control));
        Assert.That(DisconnectNotice.TryRead(message.AsSpan(1), out DisconnectNotice? notice), Is.True, "a notice");
        return notice!;
    }

    [TestCase("")]
    [TestCase("wrong")]
    [TestCase(null)]
    public void CheckRequest_WithAWrongKey_Is403_EvenWhenAdmissionIsClosedOrTheServerFull(string? key)
    {
        using var harness = new Harness(1);
        using TestWebSocketPeer _ = harness.Connect(out ConnectionId _);
        harness.Transport.CloseAdmission();

        Assert.That(harness.Transport.CheckRequest(IPAddress.Loopback, key), Is.EqualTo(403));
    }

    [TestCase(new byte[] { 3, 0x01, 0x00 }, TestName = "Message_OnAnUnknownChannel_ClosesTheConnection")]
    [TestCase(new byte[] { 255, 1, 0, 0 }, TestName = "Message_AShortHeartbeat_ClosesTheConnection")]
    [TestCase(new byte[] { 255, 3, 0, 0, 0, 0 }, TestName = "Message_AHeartbeatOfNoKind_ClosesTheConnection")]
    [TestCase(new byte[0], TestName = "Message_Empty_ClosesTheConnection")]
    public void Message_BreakingTheFraming_ClosesTheConnection(byte[] message)
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        peer.Send(message);

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
    }

    private sealed class Harness : IDisposable
    {
        private readonly List<IDisposable> m_owned = new();

        public Harness(
            int maxConnections = 8,
            AbuseOptions? abuse = null,
            ConnectionRegistry? connections = null,
            int disconnectTimeoutMs = 10000,
            bool isStarted = true)
        {
            var options = new NetworkOptions
            {
                MaxConnections = maxConnections,
                ConnectionKey = Key,
                DisconnectTimeoutMs = disconnectTimeoutMs
            };
            IOptions<AbuseOptions> limits = Options.Create(abuse ?? new AbuseOptions { Enabled = false });
            var clock = new StopwatchClock();
            ServerInstruments instruments = TestInstruments.Create();
            Inbound = new InboundQueue(
                Options.Create(options),
                limits,
                Options.Create(new SimulationOptions()),
                clock,
                instruments);
            Log = new CapturingLogger<WebSocketServerTransport>();
            Transport = new WebSocketServerTransport(
                Inbound,
                Options.Create(options),
                new AddressThrottle(limits, clock),
                connections ?? new ConnectionRegistry(Options.Create(options)),
                instruments,
                new AuditLog(new CapturingLogger<AuditLog>(), clock),
                Log);
            if (isStarted)
            {
                Transport.Start();
            }
        }

        public WebSocketServerTransport Transport { get; }

        public InboundQueue Inbound { get; }

        public CapturingLogger<WebSocketServerTransport> Log { get; }

        public List<Task> Running { get; } = new();

        public void Dispose()
        {
            Transport.Stop(DisconnectReason.Maintenance, string.Empty);
            foreach (IDisposable owned in m_owned)
            {
                owned.Dispose();
            }
        }

        /// <summary>
        ///     A connection past the gateway's checks and the admission, and its Connected event.
        /// </summary>
        public TestWebSocketPeer Connect(
            out ConnectionId connection,
            bool isAnsweringPings = true,
            bool isReading = true)
        {
            Assert.That(Transport.CheckRequest(IPAddress.Loopback, Key), Is.Zero, "the gateway would upgrade");
            (WebSocket server, TestWebSocketPeer peer) = TestWebSocketPeer.CreatePair(isAnsweringPings, isReading);
            m_owned.Add(server);
            Running.Add(Transport.RunAsync(server, IPAddress.Loopback, CancellationToken.None));
            Assert.That(WaitForEvent(InboundEventKind.Connected, out InboundEvent connected), Is.True, "connected");
            connection = connected.Connection;
            return peer;
        }

        /// <summary>
        ///     An upgraded connection the transport is expected to refuse with a notice.
        /// </summary>
        public TestWebSocketPeer ConnectRefused()
        {
            (WebSocket server, TestWebSocketPeer peer) = TestWebSocketPeer.CreatePair();
            m_owned.Add(server);
            Running.Add(Transport.RunAsync(server, IPAddress.Loopback, CancellationToken.None));
            return peer;
        }

        public bool WaitForEvent(InboundEventKind kind, TimeSpan? limit = null)
        {
            return WaitForEvent(kind, out InboundEvent _, limit);
        }

        // Skips events of other kinds, such as the Connected of a peer the test is done with.
        public bool WaitForEvent(InboundEventKind kind, out InboundEvent found, TimeSpan? limit = null)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < (limit ?? TestWebSocketPeer.Timeout))
            {
                if (Inbound.TryDequeue(out InboundEvent inboundEvent) && inboundEvent.Kind == kind)
                {
                    found = inboundEvent;
                    return true;
                }

                Thread.Sleep(5);
            }

            found = default;
            return false;
        }

        public bool WaitUntil(Func<bool> condition, TimeSpan? limit = null)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < (limit ?? TestWebSocketPeer.Timeout))
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            return false;
        }
    }

    [Test]
    public void CheckRequest_BeforeTheTransportStarts_OrAfterItStopped_Is503()
    {
        using var harness = new Harness(isStarted: false);

        int beforeStart = harness.Transport.CheckRequest(IPAddress.Loopback, Key);
        harness.Transport.Start();
        int running = harness.Transport.CheckRequest(IPAddress.Loopback, Key);
        harness.Transport.Stop(DisconnectReason.Maintenance, string.Empty);
        int afterStop = harness.Transport.CheckRequest(IPAddress.Loopback, Key);

        Assert.That(new[] { beforeStart, running, afterStop }, Is.EqualTo(new[] { 503, 0, 503 }));
    }

    [Test]
    public void CheckRequest_OverTheCapOfItsAddress_Is429_UntilAConnectionEnds()
    {
        using var harness = new Harness(abuse: new AbuseOptions { MaxConnectionsPerAddress = 1 });
        TestWebSocketPeer first = harness.Connect(out ConnectionId _);

        int whileHeld = harness.Transport.CheckRequest(IPAddress.Loopback, Key);
        first.Dispose();
        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        int afterwards = harness.Transport.CheckRequest(IPAddress.Loopback, Key);

        Assert.That(whileHeld, Is.EqualTo(429));
        Assert.That(afterwards, Is.Zero);
    }

    [Test]
    public void Connect_AfterAdmissionClosed_GetsMaintenanceAsItsOnlyMessage()
    {
        using var harness = new Harness();
        harness.Transport.CloseAdmission();

        using TestWebSocketPeer peer = harness.ConnectRefused();

        Assert.That(ReadNotice(peer.Receive()).Reason, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(harness.Inbound.TryDequeue(out InboundEvent _), Is.False, "nothing reached the queue");
    }

    [Test]
    public void Connect_FromACoolingAddress_GetsRateLimited_UntilTheCooldownEnds()
    {
        using var harness = new Harness(abuse: new AbuseOptions { KickCooldownMs = 1000 });
        TestWebSocketPeer first = harness.Connect(out ConnectionId connection);
        harness.Transport.CoolDownAddress(connection);
        first.Dispose();

        using TestWebSocketPeer refused = harness.ConnectRefused();
        DisconnectNotice notice = ReadNotice(refused.Receive());
        Thread.Sleep(1200);
        using TestWebSocketPeer later = harness.Connect(out ConnectionId _);

        Assert.That(notice.Reason, Is.EqualTo(DisconnectReason.RateLimited));
    }

    [Test]
    public void Connect_TakesItsIdFromTheSharedRegistry()
    {
        var connections = new ConnectionRegistry(Options.Create(new NetworkOptions { MaxConnections = 8 }));
        var other = new InMemoryServerTransport();
        for (int index = 0; index < 3; index++)
        {
            connections.TryAdmit(other, out ConnectionId _);
        }

        using var harness = new Harness(connections: connections);

        using TestWebSocketPeer _ = harness.Connect(out ConnectionId connection);

        Assert.That(connection.Value, Is.EqualTo(4));
        Assert.That(connections.TryGetOwner(connection, out IServerTransport? owner), Is.True);
        Assert.That(owner, Is.SameAs(harness.Transport));
    }

    [Test]
    public void Connect_WhenServerFull_GetsServerFull()
    {
        using var harness = new Harness(1);
        using TestWebSocketPeer _ = harness.Connect(out ConnectionId _);

        using TestWebSocketPeer refused = harness.ConnectRefused();

        Assert.That(ReadNotice(refused.Receive()).Reason, Is.EqualTo(DisconnectReason.ServerFull));
    }

    [Test]
    public void Connect_WhileAnotherTransportHoldsEverySlot_GetsServerFull()
    {
        var connections = new ConnectionRegistry(Options.Create(new NetworkOptions { MaxConnections = 2 }));
        var other = new InMemoryServerTransport();
        connections.TryAdmit(other, out ConnectionId _);
        connections.TryAdmit(other, out ConnectionId _);
        using var harness = new Harness(connections: connections);

        using TestWebSocketPeer refused = harness.ConnectRefused();

        Assert.That(ReadNotice(refused.Receive()).Reason, Is.EqualTo(DisconnectReason.ServerFull));
    }

    [Test]
    public void Connect_WithRightKey_EnqueuesConnected()
    {
        using var harness = new Harness();

        using TestWebSocketPeer _ = harness.Connect(out ConnectionId connection);

        Assert.That(connection.Value, Is.EqualTo(1));
        Assert.That(harness.Transport.ConnectionCount, Is.EqualTo(1));
    }

    [Test]
    public void CoolDownAddress_AfterThePeerLeftOnItsOwn_StillRefusesItsAddress()
    {
        using var harness = new Harness(abuse: new AbuseOptions { KickCooldownMs = 60000 });
        TestWebSocketPeer first = harness.Connect(out ConnectionId connection);
        first.Dispose();
        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);

        harness.Transport.CoolDownAddress(connection);

        using TestWebSocketPeer refused = harness.ConnectRefused();
        Assert.That(ReadNotice(refused.Receive()).Reason, Is.EqualTo(DisconnectReason.RateLimited));
    }

    [Test]
    public void CoolDownAddress_OfADepartureOlderThanMaxConnectionsOthers_IsForgotten()
    {
        using var harness = new Harness(2, new AbuseOptions { KickCooldownMs = 60000 });
        var departed = new List<ConnectionId>();
        for (int index = 0; index < 3; index++)
        {
            TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);
            departed.Add(connection);
            peer.Dispose();
            Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        }

        harness.Transport.CoolDownAddress(departed[0]);

        using TestWebSocketPeer admitted = harness.Connect(out ConnectionId _);
    }

    [Test]
    public void Disconnect_ByClient_EnqueuesDisconnected_AndGivesItsSlotBack()
    {
        var connections = new ConnectionRegistry(Options.Create(new NetworkOptions { MaxConnections = 8 }));
        using var harness = new Harness(connections: connections);
        TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);

        peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected, out InboundEvent left), Is.True);
        Assert.That(left.Connection, Is.EqualTo(connection));
        Assert.That(harness.WaitUntil(() => connections.Count == 0), Is.True, "the slot came back");
        peer.Dispose();
    }

    [Test]
    public void Disconnect_ByServer_OfAPeerThatNeverAnswersTheClose_EndsWithinASecond()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection, isReading: false);
        Task running = harness.Running.Single();
        var closing = Stopwatch.StartNew();

        harness.Transport.Disconnect(connection, DisconnectReason.Kicked, string.Empty);

        Assert.That(running.Wait(TimeSpan.FromSeconds(3)), Is.True, "the connection ended");
        Assert.That(closing.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900)),
            "it waited for the close");
        Assert.That(harness.Transport.ConnectionCount, Is.Zero);
    }

    [Test]
    public void Disconnect_ByServer_SendsWhatWasQueuedThenTheNoticeLast_AndEnqueuesNothing()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);

        harness.Transport.Send(connection, Despawn());
        harness.Transport.Disconnect(connection, DisconnectReason.Kicked, "bye");

        byte[]? first = peer.Receive();
        DisconnectNotice notice = ReadNotice(peer.Receive());
        Assert.That(first, Is.EqualTo(Framed(Despawn())));
        Assert.That(notice.Reason, Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(notice.Message, Is.EqualTo("bye"));
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
        Assert.That(harness.WaitUntil(() => harness.Inbound.TrackedPeers == 0), Is.True, "the queue forgot the peer");
        Assert.That(harness.Inbound.TryDequeue(out InboundEvent _), Is.False, "no Disconnected for a server close");
    }

    [Test]
    public void Heartbeat_AnsweredByTheClient_GivesARoundTripTime_UnknownBefore()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);

        bool isKnownAtOnce = harness.Transport.TryGetRoundTripTime(connection, out int _);
        int milliseconds = -1;
        bool isKnownLater = harness.WaitUntil(
            () => harness.Transport.TryGetRoundTripTime(connection, out milliseconds),
            TimeSpan.FromSeconds(3));

        Assert.That(isKnownAtOnce, Is.False);
        Assert.That(isKnownLater, Is.True);
        Assert.That(milliseconds, Is.InRange(0, 1000));
    }

    [Test]
    public void Heartbeat_FromTheClient_IsAnsweredWithItsNonce_AndNeverReachesTheQueue()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _, false);

        peer.Send(TestWebSocketPeer.Heartbeat(WebSocketServerTransport.HeartbeatPing, 0xCAFE));

        byte[]? pong = null;
        var elapsed = Stopwatch.StartNew();
        while (pong == null && elapsed.Elapsed < TestWebSocketPeer.Timeout)
        {
            byte[]? message = peer.ReceiveAny();
            if (message != null && message[1] == WebSocketServerTransport.HeartbeatPong)
            {
                pong = message;
            }
        }

        Assert.That(pong, Is.EqualTo(TestWebSocketPeer.Heartbeat(WebSocketServerTransport.HeartbeatPong, 0xCAFE)));
        Assert.That(harness.Inbound.TryDequeue(out InboundEvent _), Is.False);
    }

    // Answers to the server's pings are outside the budget: three pings a second of the peer's own and its answers to
    // the server's two stay within it, where counting them together would make five.
    [Test]
    public void Heartbeats_AnsweringTheServersPings_AreOutsideTheBudget()
    {
        using var harness = new Harness(disconnectTimeoutMs: 2000);
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        var elapsed = Stopwatch.StartNew();
        for (uint nonce = 1; elapsed.Elapsed < TimeSpan.FromSeconds(3); nonce++)
        {
            peer.Send(TestWebSocketPeer.Heartbeat(WebSocketServerTransport.HeartbeatPing, nonce));
            Thread.Sleep(340);
        }

        Assert.That(peer.WaitForClose(TimeSpan.Zero), Is.False, $"the connection closed: {peer.CloseStatus}");
    }

    [Test]
    public void Heartbeats_MoreThanFourInASecond_CloseTheConnection()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        for (uint nonce = 0; nonce < 6; nonce++)
        {
            peer.Send(TestWebSocketPeer.Heartbeat(WebSocketServerTransport.HeartbeatPong, nonce));
        }

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
    }

    [Test]
    public void Message_AsText_ClosesTheConnection()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        peer.Send(new byte[] { 0, 1, 2 }, WebSocketMessageType.Text);

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.InvalidMessageType));
    }

    [Test]
    public void Message_AtTheLimit_ReachesTheQueue_AndOneByteMoreClosesTheConnection()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        peer.Send(new byte[WebSocketServerTransport.MaxMessageBytes]);
        bool isAtTheLimitQueued = harness.WaitForEvent(InboundEventKind.Malformed);
        peer.Send(new byte[WebSocketServerTransport.MaxMessageBytes + 1]);

        Assert.That(isAtTheLimitQueued, Is.True, "the queue judges a message within the limit");
        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
    }

    [Test]
    public void Message_InFragmentsOverTheLimit_ClosesTheConnection()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);
        byte[] half = new byte[WebSocketServerTransport.MaxMessageBytes / 2 + 1];

        peer.SendFragments(half, half);

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
        Assert.That(peer.CloseStatus, Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
    }

    [Test]
    public void Message_InFragments_ReachesTheQueueWhole()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);
        byte[] move = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(1, 1, 1f, 0f)).Write(move);
        byte[] message = TestWebSocketPeer.Frame((byte)ProtocolChannel.Input, move);

        peer.SendFragments(message[..3], message[3..]);

        Assert.That(harness.WaitForEvent(InboundEventKind.Move), Is.True);
    }

    [Test]
    public void Messages_OverThePeersBudget_AreRateLimited()
    {
        using var harness = new Harness(abuse: new AbuseOptions { PeerMessageBurst = 5 });
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);

        for (int index = 0; index < 20; index++)
        {
            peer.Send(ProtocolChannel.Control, Hello());
        }

        Assert.That(harness.WaitForEvent(InboundEventKind.RateLimited, out InboundEvent limited), Is.True);
        Assert.That(limited.Connection, Is.EqualTo(connection));
    }

    [Test]
    public void Payload_FromAPeerTheServerClosed_IsIgnored()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection, false);
        harness.Transport.Disconnect(connection, DisconnectReason.Kicked, string.Empty);
        ReadNotice(peer.Receive());

        try
        {
            peer.Send(ProtocolChannel.Control, Hello());
        }
        catch (WebSocketException)
        {
            // The server may already have closed the socket.
        }

        Thread.Sleep(200);
        Assert.That(harness.Inbound.TryDequeue(out InboundEvent _), Is.False);
    }

    [Test]
    public void Payload_FromClient_IsDecodedAndEnqueued()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);

        peer.Send(ProtocolChannel.Control, Hello());

        Assert.That(harness.WaitForEvent(InboundEventKind.Hello, out InboundEvent hello), Is.True);
        Assert.That(hello.Connection, Is.EqualTo(connection));
    }

    [Test]
    public void Payload_MoveInputOnTheInputChannel_IsDecodedAsAMoveEvent()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);
        byte[] move = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(1, 1, 1f, 0f)).Write(move);

        peer.Send(ProtocolChannel.Input, move);

        Assert.That(harness.WaitForEvent(InboundEventKind.Move), Is.True);
    }

    [Test]
    public void Payload_OnTheWrongChannel_IsEnqueuedAsMalformed()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        peer.Send(ProtocolChannel.State, Hello());

        Assert.That(harness.WaitForEvent(InboundEventKind.Malformed), Is.True);
    }

    [Test]
    public void Send_OfABurstAsLargeAsACrowdedEntry_ArrivesWhole()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);
        const int messages = 700;

        for (int index = 0; index < messages; index++)
        {
            harness.Transport.Send(connection, Despawn());
        }

        int arrived = 0;
        while (arrived < messages && peer.Receive() != null)
        {
            arrived++;
        }

        Assert.That(arrived, Is.EqualTo(messages));
        Assert.That(harness.Transport.ConnectionCount, Is.EqualTo(1), "still connected");
    }

    [Test]
    public void Send_ToAPeerThatStopsReading_OverflowsItsQueueAndClosesIt()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection, isReading: false);
        byte[] large = new byte[1000];
        Despawn().CopyTo(large, 0);

        var elapsed = Stopwatch.StartNew();
        while (harness.Transport.ConnectionCount > 0 && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            harness.Transport.Send(connection, large);
        }

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected), Is.True);
        Assert.That(harness.Log.Entries.Select(entry => entry.EventId.Name), Does.Contain("WebSocketSendQueueFull"));
    }

    [Test]
    public void Send_ToConnection_ArrivesUnchangedAfterItsChannelByte()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);
        byte[] entered = new byte[CommandRejected.EncodedLength];
        new CommandRejected(7, CommandRejectionReason.InvalidTarget).Write(entered);

        harness.Transport.Send(connection, Despawn());
        harness.Transport.Send(connection, entered);

        Assert.That(peer.Receive(), Is.EqualTo(Framed(Despawn())));
        Assert.That(peer.Receive(), Is.EqualTo(Framed(entered)));
    }

    [Test]
    public void Send_ToUnknownConnection_IsIgnored()
    {
        using var harness = new Harness();

        Action send = () => harness.Transport.Send(new ConnectionId(99), Despawn());

        Assert.That(send, Throws.Nothing);
    }

    [Test]
    public void Send_WithoutARoutableOpcode_Throws()
    {
        using var harness = new Harness();
        using TestWebSocketPeer _ = harness.Connect(out ConnectionId connection);
        Action send = () => harness.Transport.Send(connection, new byte[] { 0xFF, 0xFF });

        Assert.That(send, Throws.ArgumentException);
    }

    [Test]
    public void Silence_ForTheDisconnectTimeout_ClosesTheConnection()
    {
        using var harness = new Harness(disconnectTimeoutMs: 1000);
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _, false);

        Assert.That(harness.WaitForEvent(InboundEventKind.Disconnected, TimeSpan.FromSeconds(4)), Is.True);
        Assert.That(peer.WaitForClose(), Is.True);
    }

    [Test]
    public void Silence_OfAClientThatAnswersItsPings_NeverClosesIt()
    {
        // Two and a half timeouts, with a timeout long enough that a busy test machine still answers in time.
        using var harness = new Harness(disconnectTimeoutMs: 2000);
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId _);

        Thread.Sleep(5000);

        Assert.That(harness.Transport.ConnectionCount, Is.EqualTo(1));
        Assert.That(peer.Closed.IsSet, Is.False);
    }

    [Test]
    public void Statistics_AfterTraffic_CountBytesAndMessagesBothWays()
    {
        using var harness = new Harness();
        using TestWebSocketPeer peer = harness.Connect(out ConnectionId connection);
        peer.Send(ProtocolChannel.Control, Hello());
        Assert.That(harness.WaitForEvent(InboundEventKind.Hello), Is.True);
        harness.Transport.Send(connection, Despawn());
        Assert.That(peer.Receive(), Is.Not.Null);

        TransportStatistics statistics = harness.Transport.GetStatistics();

        Assert.That(statistics.BytesReceived, Is.GreaterThanOrEqualTo(Hello().Length + 1));
        Assert.That(statistics.BytesSent, Is.GreaterThanOrEqualTo(EntityDespawn.EncodedLength + 1));
        Assert.That(statistics.PacketsReceived, Is.GreaterThanOrEqualTo(1));
        Assert.That(statistics.PacketsSent, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Stop_WithConnectedPeers_DeliversTheNotice_AndAbortsWhatStaysOpenWithinTheAllowance()
    {
        using var harness = new Harness();
        using TestWebSocketPeer polite = harness.Connect(out ConnectionId _);
        using TestWebSocketPeer silent = harness.Connect(out ConnectionId _, false);

        var stopping = Stopwatch.StartNew();
        harness.Transport.Stop(DisconnectReason.Maintenance, "back soon");
        TimeSpan stopped = stopping.Elapsed;

        DisconnectNotice notice = ReadNotice(polite.Receive());
        Assert.That(notice.Reason, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(notice.Message, Is.EqualTo("back soon"));
        Assert.That(stopped, Is.LessThan(TimeSpan.FromSeconds(2)), "within the allowance");
        Assert.That(harness.WaitUntil(() => harness.Transport.ConnectionCount == 0), Is.True);
    }
}
}
