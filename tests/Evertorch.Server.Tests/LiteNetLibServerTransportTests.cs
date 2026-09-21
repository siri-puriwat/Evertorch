using System;
using System.Diagnostics;
using System.Threading;
using Evertorch.Game;
using Evertorch.Protocol;
using LiteNetLib;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
/// Real UDP sockets on loopback with an operating-system-chosen port. Every wait is bounded.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LiteNetLibServerTransportTests
{
    private const string Key = "evertorch";

    [Test]
    public void Connect_WithRightKey_EnqueuesConnected()
    {
        using Harness harness = new Harness();
        using TestNetClient client = new TestNetClient();

        client.Connect(harness.Port, Key);

        Assert.That(client.WaitFor(() => client.IsConnected), Is.True);
        Assert.That(harness.WaitForEvent(out InboundEvent connected), Is.True);
        Assert.That(connected.Kind, Is.EqualTo(InboundEventKind.Connected));
    }

    [Test]
    public void Connect_WithWrongKey_IsRejected()
    {
        using Harness harness = new Harness();
        using TestNetClient client = new TestNetClient();

        client.Connect(harness.Port, "not-the-key");

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.TransportReason, Is.EqualTo(LiteNetLib.DisconnectReason.ConnectionRejected));
        Assert.That(harness.Inbound.Count, Is.EqualTo(0));
    }

    [Test]
    public void Connect_WhenServerFull_IsRejectedWithServerFullNotice()
    {
        using Harness harness = new Harness(maxConnections: 1);
        using TestNetClient first = new TestNetClient();
        using TestNetClient second = new TestNetClient();
        first.Connect(harness.Port, Key);
        Assert.That(first.WaitFor(() => first.IsConnected), Is.True);

        second.Connect(harness.Port, Key);

        Assert.That(second.WaitFor(() => second.IsDisconnected), Is.True);
        Assert.That(second.Notice, Is.Not.Null);
        Assert.That(second.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.ServerFull));
        Assert.That(first.IsConnected, Is.True);
    }

    [Test]
    public void Connect_AfterAdmissionClosed_IsRejectedWithMaintenance()
    {
        using Harness harness = new Harness();
        using TestNetClient client = new TestNetClient();
        harness.Transport.CloseAdmission();

        client.Connect(harness.Port, Key);

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.Maintenance));
    }

    [Test]
    public void Payload_FromClient_IsDecodedAndEnqueued()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        client.Send(Hello(), ProtocolChannel.Control, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent hello), Is.True);
        Assert.That(hello.Kind, Is.EqualTo(InboundEventKind.Hello));
        Assert.That(hello.Connection, Is.EqualTo(connection));
        Assert.That(hello.Hello!.SessionToken, Is.EqualTo("dev:tester"));
    }

    [Test]
    public void Payload_OnTheWrongChannel_IsEnqueuedAsMalformed()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId _);

        client.Send(Hello(), ProtocolChannel.Input, DeliveryMethod.ReliableOrdered);

        Assert.That(harness.WaitForEvent(out InboundEvent malformed), Is.True);
        Assert.That(malformed.Kind, Is.EqualTo(InboundEventKind.Malformed));
    }

    [Test]
    public void Send_ToConnection_ArrivesUnchangedOnItsRoutedChannelAndDelivery()
    {
        using Harness harness = new Harness();
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
    public void Send_OfTheLargestSnapshot_FitsOneUnreliableDatagramAndArrivesOnTheStateChannel()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        EntitySnapshot largest = new EntitySnapshot(1, 1, new EntityState[EntitySnapshot.MaxEntities]);
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
    public void Payload_MoveInputOnTheInputChannel_IsDecodedAsAMoveEvent()
    {
        using Harness harness = new Harness();
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
    public void Send_ToUnknownConnection_IsIgnored()
    {
        using Harness harness = new Harness();
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(77), DespawnReason.Removed).Write(despawn);
        Action send = () => harness.Transport.Send(new ConnectionId(12345), despawn);

        Assert.That(send, Throws.Nothing);
    }

    [Test]
    public void Send_WithoutARoutableOpcode_Throws()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);
        Action send = () => harness.Transport.Send(connection, new byte[] { 0xFF, 0xFF });

        Assert.That(send, Throws.ArgumentException);
    }

    [Test]
    public void Disconnect_ByServer_DeliversTheNoticeAndEnqueuesNothing()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        harness.Transport.Disconnect(connection, Protocol.DisconnectReason.SessionReplaced, "Signed in elsewhere");

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.SessionReplaced));
        Assert.That(client.Notice.Message, Is.EqualTo("Signed in elsewhere"));
        Thread.Sleep(100);
        Assert.That(harness.Inbound.Count, Is.EqualTo(0));
    }

    [Test]
    public void Disconnect_ByClient_EnqueuesDisconnected()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId connection);

        client.Disconnect();

        Assert.That(harness.WaitForEvent(out InboundEvent disconnected), Is.True);
        Assert.That(disconnected.Kind, Is.EqualTo(InboundEventKind.Disconnected));
        Assert.That(disconnected.Connection, Is.EqualTo(connection));
    }

    [Test]
    public void Stop_WithConnectedPeer_DeliversMaintenanceNotice()
    {
        using Harness harness = new Harness();
        using TestNetClient client = ConnectedClient(harness, out ConnectionId _);

        harness.Transport.Stop();

        Assert.That(client.WaitFor(() => client.IsDisconnected), Is.True);
        Assert.That(client.Notice!.Reason, Is.EqualTo(Protocol.DisconnectReason.Maintenance));
    }

    [Test]
    public void Statistics_AfterTraffic_ReportBytesBothWaysAndARoundTripForKnownConnections()
    {
        using Harness harness = new Harness();
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
    public void Start_WhenPortIsTaken_Throws()
    {
        using Harness harness = new Harness();
        using LiteNetLibServerTransport second = Harness.CreateTransport(harness.Port, 4, out InboundQueue _);
        Action start = () => second.Start();

        Assert.That(start, Throws.InvalidOperationException);
    }

    private static TestNetClient ConnectedClient(Harness harness, out ConnectionId connection)
    {
        TestNetClient client = new TestNetClient();
        client.Connect(harness.Port, Key);
        Assert.That(client.WaitFor(() => client.IsConnected), Is.True, "the client did not connect");
        Assert.That(harness.WaitForEvent(out InboundEvent connected), Is.True, "no Connected event arrived");
        connection = connected.Connection;
        return client;
    }

    private static byte[] Hello()
    {
        ClientHello hello = new ClientHello(1, "0.2.0-dev", 1, "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private sealed class Harness : IDisposable
    {
        public Harness(int maxConnections = 8)
        {
            Transport = CreateTransport(0, maxConnections, out InboundQueue inbound);
            Inbound = inbound;
            Transport.Start();
        }

        public LiteNetLibServerTransport Transport { get; }

        public InboundQueue Inbound { get; }

        public int Port => Transport.LocalPort;

        public static LiteNetLibServerTransport CreateTransport(int port, int maxConnections, out InboundQueue inbound)
        {
            NetworkOptions options = new NetworkOptions { Port = port, MaxConnections = maxConnections };
            inbound = new InboundQueue(Options.Create(options));
            return new LiteNetLibServerTransport(
                inbound,
                Options.Create(options),
                new CapturingLogger<LiteNetLibServerTransport>());
        }

        public bool WaitForEvent(out InboundEvent inboundEvent)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
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

        public void Dispose()
        {
            Transport.Dispose();
        }
    }
}
}
