using System;
using System.Diagnostics;
using System.Threading;
using Evertorch.Game;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using DisconnectReason = Evertorch.Protocol.DisconnectReason;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Real UDP sockets on loopback with an operating-system-chosen port. Every wait is bounded.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LiteNetLibServerTransportTests
{
    private const string Key = "evertorch";

    private static TestNetClient ConnectedClient(Harness harness, out ConnectionId connection)
    {
        var client = new TestNetClient();
        client.Connect(harness.Port, Key);
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True, "the client did not connect");
        Assert.That(harness.WaitForEvent(out InboundEvent connected), Is.True, "no Connected event arrived");
        connection = connected.Connection;
        return client;
    }

    private static byte[] Hello()
    {
        var hello = new ClientHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, 1, "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private sealed class Harness : IDisposable
    {
        public Harness(int maxConnections = 8, AbuseOptions? abuse = null)
        {
            Transport = CreateTransport(0, maxConnections, out InboundQueue inbound, abuse);
            Inbound = inbound;
            Transport.Start();
        }

        public LiteNetLibServerTransport Transport { get; }

        public InboundQueue Inbound { get; }

        public int Port => Transport.LocalPort;

        public void Dispose()
        {
            Transport.Dispose();
        }

        public static LiteNetLibServerTransport CreateTransport(
            int port,
            int maxConnections,
            out InboundQueue inbound,
            AbuseOptions? abuse = null)
        {
            var options = new NetworkOptions { Port = port, MaxConnections = maxConnections };
            IOptions<AbuseOptions> limits = Options.Create(abuse ?? new AbuseOptions { Enabled = false });
            var clock = new StopwatchClock();
            ServerInstruments instruments = TestInstruments.Create();
            inbound = new InboundQueue(
                Options.Create(options),
                limits,
                Options.Create(new SimulationOptions()),
                clock,
                instruments);
            return new LiteNetLibServerTransport(
                inbound,
                Options.Create(options),
                new AddressThrottle(limits, clock),
                instruments,
                new AuditLog(new CapturingLogger<AuditLog>(), clock),
                new CapturingLogger<LiteNetLibServerTransport>());
        }

        public bool WaitForEvent(out InboundEvent inboundEvent)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (Inbound.TryDequeue(out inboundEvent))
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            inboundEvent = default;
            return false;
        }
    }

    [Test]
    public void Connect_AfterAdmissionClosed_IsRejectedWithMaintenance()
    {
        using var harness = new Harness();
        using var client = new TestNetClient();
        harness.Transport.CloseAdmission();

        client.Connect(harness.Port, Key);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
    }

    [Test]
    public void Connect_FromACoolingAddress_IsRejectedWithRateLimitedAfterTheKeyCheckUntilTheCooldownEnds()
    {
        using var harness = new Harness(abuse: new AbuseOptions { KickCooldownMs = 1000 });
        using TestNetClient kicked = ConnectedClient(harness, out ConnectionId connection);
        var cooling = Stopwatch.StartNew();
        harness.Transport.CoolDownAddress(connection);
        harness.Transport.Disconnect(connection, DisconnectReason.Kicked, string.Empty);
        Assert.That(kicked.WaitFor(() => kicked.IsDisconnected), Is.True);

        using var again = new TestNetClient();
        using var keyless = new TestNetClient();
        again.Connect(harness.Port, Key);
        keyless.Connect(harness.Port, "not-the-key");
        Assert.That(again.WaitFor(() => again.IsDisconnected), Is.True);
        Assert.That(keyless.WaitFor(() => keyless.IsDisconnected), Is.True);
        Assert.That(cooling.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "the rejections came within the cooldown");

        Thread.Sleep(TimeSpan.FromMilliseconds(1100) - cooling.Elapsed);
        using var later = new TestNetClient();
        later.Connect(harness.Port, Key);

        Assert.That(again.Notice!.Reason, Is.EqualTo(DisconnectReason.RateLimited));
        Assert.That(keyless.Notice, Is.Null, "the key is checked before the cooldown");
        Assert.That(later.WaitFor(() => later.IsConnected), Is.True, "admitted once the cooldown is over");
    }

    [Test]
    public void Connect_OverTheCapOfItsAddress_IsRefusedWithoutAWordUntilAConnectionEnds()
    {
        var abuse = new AbuseOptions { MaxConnectionsPerAddress = 1 };
        using LiteNetLibServerTransport transport = Harness.CreateTransport(0, 8, out InboundQueue _, abuse);
        transport.Start();
        using var first = new TestNetClient();
        using var second = new TestNetClient();
        first.Connect(transport.LocalPort, Key);
        Assert.That(first.WaitFor(() => first.IsConnected), Is.True);

        second.Connect(transport.LocalPort, Key);
        bool isAdmitted = second.WaitFor(() => second.IsConnected || second.IsDisconnected, TimeSpan.FromSeconds(1));
        first.Disconnect();

        Assert.That(isAdmitted, Is.False, "refused without a word: no connection and no rejection");
        Assert.That(second.WaitFor(() => second.IsConnected), Is.True, "a retry succeeds once the address has room");
    }

    [Test]
    public void Connect_WhenServerFull_IsRejectedWithServerFullNotice()
    {
        using var harness = new Harness(1);
        using var first = new TestNetClient();
        using var second = new TestNetClient();
        first.Connect(harness.Port, Key);
        Assert.That(first.WaitFor(() => first.IsConnected), Is.True);

        second.Connect(harness.Port, Key);

        Assert.That(second.WaitFor(() => second.IsDisconnected), Is.True);
        Assert.That(second.Notice, Is.Not.Null);
        Assert.That(second.Notice!.Reason, Is.EqualTo(DisconnectReason.ServerFull));
        Assert.That(first.IsConnected, Is.True);
    }

    [Test]
    public void Connect_WithRightKey_EnqueuesConnected()
    {
        using var harness = new Harness();
        using var client = new TestNetClient();

        client.Connect(harness.Port, Key);

        Assert.That(client.WaitFor(() => client.IsConnected), Is.True);
        Assert.That(harness.WaitForEvent(out InboundEvent connected), Is.True);
        Assert.That(connected.Kind, Is.EqualTo(InboundEventKind.Connected));
    }

    [Test]
    public void Connect_WithWrongKeyAfterAdmissionClosed_IsRejectedWithoutANotice()
    {
        using var harness = new Harness();
        using var client = new TestNetClient();
        harness.Transport.CloseAdmission();

        client.Connect(harness.Port, "not-the-key");

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.TransportReason, Is.EqualTo(LiteNetLib.DisconnectReason.ConnectionRejected));
        Assert.That(client.Notice, Is.Null, "a requester without the key learns nothing about the server's state");
    }

    [Test]
    public void Connect_WithWrongKeyWhenFull_IsRejectedWithoutANotice()
    {
        using var harness = new Harness(1);
        using var first = new TestNetClient();
        using var second = new TestNetClient();
        first.Connect(harness.Port, Key);
        Assert.That(first.WaitFor(() => first.IsConnected), Is.True);

        second.Connect(harness.Port, "not-the-key");

        Assert.That(second.WaitFor(() => second.IsDisconnected), Is.True);
        Assert.That(second.Notice, Is.Null);
    }

    [Test]
    public void Connect_WithWrongKey_IsRejected()
    {
        using var harness = new Harness();
        using var client = new TestNetClient();

        client.Connect(harness.Port, "not-the-key");

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.TransportReason, Is.EqualTo(LiteNetLib.DisconnectReason.ConnectionRejected));
        Assert.That(harness.Inbound.Count, Is.EqualTo(0));
    }

    [Test]
    public void Disconnect_ByClient_EnqueuesDisconnected()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        client.Disconnect();

        Assert.That(harness.WaitForEvent(out InboundEvent disconnected), Is.True);
        Assert.That(disconnected.Kind, Is.EqualTo(InboundEventKind.Disconnected));
        Assert.That(disconnected.Connection, Is.EqualTo(connection));
    }

    [Test]
    public void Disconnect_ByServer_DeliversTheNoticeAndEnqueuesNothing()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        harness.Transport.Disconnect(connection, DisconnectReason.SessionReplaced, "Signed in elsewhere");

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(DisconnectReason.SessionReplaced));
        Assert.That(client.Notice.Message, Is.EqualTo("Signed in elsewhere"));
        Thread.Sleep(100);
        Assert.That(harness.Inbound.Count, Is.EqualTo(0));
    }

    [Test]
    public void Disconnect_ByServer_LetsTheQueueForgetThePeer()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        harness.Transport.Disconnect(connection, DisconnectReason.Kicked, string.Empty);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.WaitFor(() => harness.Inbound.TrackedPeers == 0), Is.True, "no budget is left behind");
    }

    [Test]
    public void Payload_FromClient_IsDecodedAndEnqueued()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        client.Send(Hello(), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent hello), Is.True);
        Assert.That(hello.Kind, Is.EqualTo(InboundEventKind.Hello));
        Assert.That(hello.Connection, Is.EqualTo(connection));
        Assert.That(hello.Hello!.SessionToken, Is.EqualTo("dev:tester"));
    }

    [Test]
    public void Payload_MoveInputOnTheInputChannel_IsDecodedAsAMoveEvent()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        byte[] payload = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(4, 8, 0f, 1f)).Write(payload);

        client.Send(payload, ProtocolChannel.Input, DeliveryMethod.Sequenced);

        Assert.That(harness.WaitForEvent(out InboundEvent move), Is.True);
        Assert.That(move.Kind, Is.EqualTo(InboundEventKind.Move));
        Assert.That(move.Connection, Is.EqualTo(connection));
        Assert.That(move.Intent, Is.EqualTo(new MoveIntent(4, 8, 0f, 1f)));
    }

    [Test]
    public void Payload_OnTheWrongChannel_IsEnqueuedAsMalformed()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId _);

        client.Send(Hello(), ProtocolChannel.Input, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent malformed), Is.True);
        Assert.That(malformed.Kind, Is.EqualTo(InboundEventKind.Malformed));
    }

    [Test]
    public void Payload_TooLargeForOneDatagram_IsDroppedByTheTransportBeforeAnythingIsReassembled()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId _);
        long malformedBefore = harness.Inbound.Malformed;

        // The test client still fragments, as a hostile peer would; the server must refuse the pieces.
        client.Send(new byte[5000], ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        client.Send(new byte[] { 0xFF, 0xFF }, ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent marker), Is.True, "the channel itself still works");
        Assert.That(marker.Kind, Is.EqualTo(InboundEventKind.Malformed));
        Assert.That(
            harness.Inbound.Malformed,
            Is.EqualTo(malformedBefore + 1),
            "only the small marker reached the queue; the 5000-byte message was never put back together");
    }

    [Test]
    public void Payload_WithAnotherDeliveryThanItsRoute_IsEnqueuedAsMalformed()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        var move = new MoveInput(new MoveIntent(1, 1, 1f, 0f));
        byte[] payload = new byte[MoveInput.EncodedLength];
        move.Write(payload);

        client.Send(payload, ProtocolChannel.Input, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent received), Is.True);
        Assert.That(received.Kind, Is.EqualTo(InboundEventKind.Malformed));
        Assert.That(received.Connection, Is.EqualTo(connection));
    }

    [Test]
    public void Send_OfTheLargestSnapshot_FitsOneUnreliableDatagramAndArrivesOnTheStateChannel()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        var largest = new EntitySnapshot(1, 1, new EntityState[EntitySnapshot.MaxEntities]);
        byte[] payload = new byte[largest.GetEncodedLength()];
        largest.Write(payload);

        harness.Transport.Send(connection, payload);

        Assert.That(client.MaxUnreliablePayload, Is.GreaterThanOrEqualTo(EntitySnapshot.MaxEncodedLength));
        Assert.That(client.WaitFor(() => client.Received.Count == 1), Is.True);
        Assert.That(client.Received[0].Payload, Is.EqualTo(payload));
        Assert.That(client.Received[0].Channel, Is.EqualTo((byte)ProtocolChannel.State));
        Assert.That(client.Received[0].Method, Is.EqualTo(DeliveryMethod.Sequenced));
    }

    [Test]
    public void Send_ToConnection_ArrivesUnchangedOnItsRoutedChannelAndDelivery()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(77), DespawnReason.Removed).Write(despawn);

        harness.Transport.Send(connection, despawn);

        Assert.That(client.WaitFor(() => client.Received.Count == 1), Is.True);
        Assert.That(client.Received[0].Payload, Is.EqualTo(despawn));
        Assert.That(client.Received[0].Channel, Is.EqualTo((byte)ProtocolChannel.Control));
        Assert.That(client.Received[0].Method, Is.EqualTo(DeliveryMethod.ReliableOrdered));
    }

    [Test]
    public void Send_ToUnknownConnection_IsIgnored()
    {
        using var harness = new Harness();
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(77), DespawnReason.Removed).Write(despawn);
        Action send = () => harness.Transport.Send(new ConnectionId(12345), despawn);

        Assert.That(send, Throws.Nothing);
    }

    [Test]
    public void Send_WithoutARoutableOpcode_Throws()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        Action send = () => harness.Transport.Send(connection, new byte[] { 0xFF, 0xFF });

        Assert.That(send, Throws.ArgumentException);
    }

    [Test]
    public void Start_WhenPortIsTaken_Throws()
    {
        using var harness = new Harness();
        using LiteNetLibServerTransport second = Harness.CreateTransport(harness.Port, 4, out InboundQueue _);
        Action start = () => second.Start();

        Assert.That(start, Throws.InvalidOperationException);
    }

    [Test]
    public void Statistics_AfterTraffic_ReportBytesBothWaysAndARoundTripForKnownConnections()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        client.Send(Hello(), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);
        Assert.That(harness.WaitForEvent(out InboundEvent _), Is.True);
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(despawn);
        harness.Transport.Send(connection, despawn);
        Assert.That(client.WaitFor(() => client.Received.Count == 1), Is.True);

        TransportStatistics statistics = harness.Transport.GetStatistics();
        bool hasRoundTrip = harness.Transport.TryGetRoundTripTime(connection, out int milliseconds);
        bool hasUnknownRoundTrip = harness.Transport.TryGetRoundTripTime(new ConnectionId(999), out int _);

        Assert.That(statistics.BytesReceived, Is.GreaterThan(0));
        Assert.That(statistics.BytesSent, Is.GreaterThan(0));
        Assert.That(statistics.PacketsReceived, Is.GreaterThan(0));
        Assert.That(hasRoundTrip, Is.True);
        Assert.That(milliseconds, Is.InRange(0, 1000));
        Assert.That(hasUnknownRoundTrip, Is.False);
    }

    [Test]
    public void Stop_WithConnectedPeer_DeliversMaintenanceNotice()
    {
        using var harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId _);

        harness.Transport.Stop(DisconnectReason.Maintenance);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
    }
}
}
