using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class InboundQueueTests
{
    private static readonly ConnectionId Peer = new(1);

    [TestCase(new byte[0])]
    [TestCase(new byte[] { 0x01 })]
    [TestCase(new byte[] { 0x00, 0x00 })]
    [TestCase(new byte[] { 0x99, 0x00, 0x01 })]
    [TestCase(new byte[] { 0x01, 0x00, 0x01 })]
    [TestCase(new byte[] { 0x02, 0x00, 0x01, 0x02 })]
    public void Message_ThatIsNotAWellFormedClientMessage_IsRejected(byte[] payload)
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, payload);

        AssertOnlyMalformed(queue, 1);
    }

    private static InboundQueue CreateQueue(int capacity)
    {
        return new InboundQueue(Options.Create(new NetworkOptions { MaxInboundEvents = capacity }));
    }

    private static byte[] Hello()
    {
        var hello = new ClientHello(1, "0.2.0-dev", 1, "dev:tester");
        byte[] payload = new byte[hello.GetEncodedLength()];
        hello.Write(payload);
        return payload;
    }

    private static void AssertOnlyMalformed(InboundQueue queue, int expectedCount)
    {
        Assert.That(queue.Malformed, Is.EqualTo(expectedCount));
        Assert.That(queue.Count, Is.EqualTo(expectedCount));
        while (queue.TryDequeue(out InboundEvent inboundEvent))
        {
            Assert.That(inboundEvent.Kind, Is.EqualTo(InboundEventKind.Malformed));
        }
    }

    [Test]
    public void Fault_WhileHandlingOnePeer_ClosesThatPeerAndTheTickGoesOn()
    {
        var server = new TestServer();
        ConnectionId faulty = server.EnterWorld(1);
        ConnectionId healthy = server.EnterWorld(2);
        server.Transport.ClearSent();
        server.Transport.FailSendsTo.Add(faulty);

        // A living player's respawn is refused at once, from inside the handling of that peer's input; the answer to
        // the faulty peer is what fails. Database results have a fault boundary of their own and are not used here.
        server.SendRespawn(faulty, 1);
        server.SendRespawn(healthy, 1);
        server.Tick();

        Assert.That(server.Transport.Disconnects[faulty], Is.EqualTo(DisconnectReason.InternalError));
        Assert.That(server.Sessions.TryGet(faulty, out _), Is.False);
        Assert.That(
            server.Transport.ControlOpcodesSentTo(healthy),
            Does.Contain(MessageOpcode.CommandRejected),
            "the next peer's input was still handled in the same tick");
        Assert.That(server.Log.Entries.Count(entry => entry.Level == LogLevel.Error), Is.EqualTo(1));
        Assert.That(server.Log.Entries.Single(entry => entry.Level == LogLevel.Error).EventId.Name,
            Is.EqualTo("SessionFaulted"));
    }

    [Test]
    public void Inbound_WhenQueueFull_DropsAndCounts()
    {
        InboundQueue queue = CreateQueue(16);

        for (int index = 0; index < 20; index++)
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        }

        Assert.That(queue.Count, Is.EqualTo(16));
        Assert.That(queue.Dropped, Is.EqualTo(4));
    }

    [Test]
    public void Inbound_WhenQueueFull_StillAcceptsConnectionLifecycleEvents()
    {
        InboundQueue queue = CreateQueue(16);
        for (int index = 0; index < 16; index++)
        {
            queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        }

        queue.OnDisconnected(Peer);
        queue.OnConnected(new ConnectionId(2));

        Assert.That(queue.Count, Is.EqualTo(18));
        Assert.That(queue.Dropped, Is.EqualTo(0));
    }

    [Test]
    public void Input_FromAConnectionTheServerNeverSaw_IsIgnored()
    {
        var server = new TestServer();

        server.SendHello(new ConnectionId(999));
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Sessions.Sessions, Is.Empty);
    }

    [Test]
    public void MalformedInput_FromAPeer_IsCountedAndChangesNothing()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(3);
        server.Transport.ClearSent();

        server.Inbound.OnPayload(connection, ProtocolChannel.Control, new byte[] { 0xFF, 0xFF, 0x00 });
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.Transport.Disconnects, Is.Empty);
        Assert.That(server.Sessions.TryGet(connection, out _), Is.True);
    }

    [Test]
    public void Message_LargerThanAnyClientPayload_IsRejectedBeforeDecoding()
    {
        InboundQueue queue = CreateQueue(16);
        byte[] oversized = new byte[ProtocolLimits.MaxClientPayloadBytes + 1];
        oversized[0] = 0x01;

        queue.OnPayload(Peer, ProtocolChannel.Control, oversized);

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void Message_OnWrongChannel_IsRejected()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Input, Hello());
        queue.OnPayload(Peer, ProtocolChannel.State, Hello());

        AssertOnlyMalformed(queue, 2);
    }

    [Test]
    public void Message_ThatOnlyAServerSends_IsRejected()
    {
        InboundQueue queue = CreateQueue(16);
        byte[] despawn = new byte[EntityDespawn.EncodedLength];
        new EntityDespawn(new EntityId(1), DespawnReason.Removed).Write(despawn);

        queue.OnPayload(Peer, ProtocolChannel.Control, despawn);

        AssertOnlyMalformed(queue, 1);
    }

    [Test]
    public void OnPayload_ForWellFormedHello_EnqueuesADecodedEvent()
    {
        InboundQueue queue = CreateQueue(16);

        queue.OnPayload(Peer, ProtocolChannel.Control, Hello());

        Assert.That(queue.TryDequeue(out InboundEvent inboundEvent), Is.True);
        Assert.That(inboundEvent.Kind, Is.EqualTo(InboundEventKind.Hello));
        Assert.That(inboundEvent.Connection, Is.EqualTo(Peer));
        Assert.That(inboundEvent.Hello!.ClientBuildVersion, Is.EqualTo("0.2.0-dev"));
        Assert.That(queue.Malformed, Is.EqualTo(0));
    }

    [Test]
    public void TryDequeue_ReturnsEventsInArrivalOrderAndTracksCount()
    {
        InboundQueue queue = CreateQueue(16);
        queue.OnConnected(Peer);
        queue.OnPayload(Peer, ProtocolChannel.Control, Hello());
        queue.OnDisconnected(Peer);

        var kinds = new InboundEventKind[3];
        for (int index = 0; index < kinds.Length; index++)
        {
            queue.TryDequeue(out InboundEvent inboundEvent);
            kinds[index] = inboundEvent.Kind;
        }

        InboundEventKind[] expected =
            { InboundEventKind.Connected, InboundEventKind.Hello, InboundEventKind.Disconnected };
        Assert.That(kinds, Is.EqualTo(expected));
        Assert.That(queue.Count, Is.EqualTo(0));
        Assert.That(queue.TryDequeue(out _), Is.False);
    }
}
}
