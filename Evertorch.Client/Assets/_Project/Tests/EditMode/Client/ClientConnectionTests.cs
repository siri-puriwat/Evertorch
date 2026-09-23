using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientConnectionTests
{
    private const string ContentVersion = "11326bd1bdfe0c49";
    private const string Token = "dev:tester-secret";

    private static readonly WorldPosition Start = ClientTestGrids.Center(2, 8);

    [Test]
    public void Connect_ThenConnected_SendsTheHelloOnTheControlChannel()
    {
        Harness harness = new Harness();

        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Single();
        Assert.That(harness.Transport.Host, Is.EqualTo("127.0.0.1"));
        Assert.That(harness.Transport.Port, Is.EqualTo(7777));
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(sent.Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(ClientHello.TryRead(sent.Payload, out ClientHello? hello), Is.True);
        Assert.That(hello!.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
        Assert.That(hello.ClientBuildVersion, Is.EqualTo("0.2.0-dev"));
        Assert.That(hello.ClientContentVersion, Is.EqualTo(0x11326bd1u));
        Assert.That(hello.SessionToken, Is.EqualTo(Token));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.AwaitingHello));
    }

    [Test]
    public void ServerHello_IsAnsweredWithTheEnterWorldRequest()
    {
        Harness harness = new Harness();
        harness.ConnectAndReceiveHello();

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(EnterWorldRequest.TryRead(sent.Payload, out EnterWorldRequest request), Is.True);
        Assert.That(request.Character, Is.EqualTo(new CharacterId(7)));
        Assert.That(harness.Connection.ServerTickRate, Is.EqualTo(20u));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.EnteringWorld));
    }

    [Test]
    public void WorldEntered_CreatesTheWorldAndAnnouncesIt()
    {
        Harness harness = new Harness();
        List<ClientWorld> entered = new List<ClientWorld>();
        harness.Connection.EnteredWorld += entered.Add;

        harness.EnterWorld();

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
        Assert.That(entered.Single(), Is.SameAs(harness.Connection.World));
        Assert.That(harness.Connection.World!.Predictor.Position, Is.EqualTo(Start));
    }

    [Test]
    public void WorldEntered_ForAMapTheClientDoesNotHave_GivesUpWithAClearError()
    {
        Harness harness = new Harness { HasMap = false };

        harness.EnterWorld();

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.World, Is.Null);
        Assert.That(harness.Connection.LocalError, Does.Contain("map.training_ground"));
        Assert.That(harness.Transport.DisconnectCalls, Is.EqualTo(1));
        Assert.That(harness.ClosedCount, Is.EqualTo(1), "closing is announced once, not once per cause");
    }

    [Test]
    public void SpawnDespawnAndSnapshot_ReachTheWorld()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        EntityId other = new EntityId(200);
        EntitySpawn spawn = new EntitySpawn(
            other,
            EntityKind.Player,
            "job.adventurer",
            ClientTestGrids.Center(3, 8),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0);
        EntitySnapshot snapshot = ClientWorldFixture.Snapshot(
            5,
            0,
            ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start),
            ClientWorldFixture.State(other, ClientTestGrids.Center(4, 8)));

        harness.Deliver(ProtocolChannel.Control, Encode(spawn.GetEncodedLength(), spawn.Write));
        harness.Deliver(ProtocolChannel.State, Encode(snapshot.GetEncodedLength(), snapshot.Write));
        bool wasKnown = harness.Connection.World!.Remotes.ContainsKey(other);
        int states = harness.Connection.World.Remotes[other].Buffer.Count;
        EntityDespawn despawn = new EntityDespawn(other, DespawnReason.OutOfRange);
        harness.Deliver(ProtocolChannel.Control, Encode(EntityDespawn.EncodedLength, despawn.Write));

        Assert.That(wasKnown, Is.True);
        Assert.That(states, Is.EqualTo(2));
        Assert.That(harness.Connection.World.Remotes.Count, Is.EqualTo(0));
        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(0));
    }

    [Test]
    public void TargetChanged_ReachesTheWorld_AndSendTargetGoesOutReliably()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        EntityId slime = new EntityId(300);
        EntitySpawn spawn = new EntitySpawn(
            slime,
            EntityKind.Monster,
            "monster.training_slime",
            ClientTestGrids.Center(4, 8),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            1000);
        harness.Deliver(ProtocolChannel.Control, Encode(spawn.GetEncodedLength(), spawn.Write));

        harness.Connection.SendTarget(slime);
        TargetChanged changed = new TargetChanged(ClientWorldFixture.LocalEntity, slime);
        harness.Deliver(ProtocolChannel.Control, Encode(TargetChanged.EncodedLength, changed.Write));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(sent.Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(TargetEntity.TryRead(sent.Payload, out TargetEntity request), Is.True);
        Assert.That(request.Target, Is.EqualTo(slime));
        Assert.That(harness.Connection.World!.Target, Is.EqualTo(slime));
    }

    [Test]
    public void Commands_ShareOneSequenceStartingAtOne_OnTheReliableChannel()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendAttack(new EntityId(300));
        harness.Connection.SendCancel();
        harness.Connection.SendRespawn();

        FakeClientTransport.SentMessage[] sent = harness.Transport.Sent.Skip(before).ToArray();
        Assert.That(sent.Select(message => message.Channel), Is.All.EqualTo(ProtocolChannel.Control));
        Assert.That(AttackEntity.TryRead(sent[0].Payload, out AttackEntity attack), Is.True);
        Assert.That(CancelAction.TryRead(sent[1].Payload, out CancelAction cancel), Is.True);
        Assert.That(Respawn.TryRead(sent[2].Payload, out Respawn respawn), Is.True);
        Assert.That(attack.Target, Is.EqualTo(new EntityId(300)));
        Assert.That(
            new[] { attack.CommandSequence, cancel.CommandSequence, respawn.CommandSequence },
            Is.EqualTo(new[] { 1u, 2u, 3u }));
    }

    [Test]
    public void Commands_BeforeTheWorldIsEntered_SendNothing()
    {
        Harness harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendAttack(new EntityId(300));
        harness.Connection.SendCancel();
        harness.Connection.SendRespawn();

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void SendTarget_BeforeTheWorldIsEntered_SendsNothing()
    {
        Harness harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendTarget(new EntityId(300));

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void Send_MovingIntent_GoesOutAsMoveInputOnTheInputChannel()
    {
        Harness harness = new Harness();
        harness.EnterWorld();

        harness.Connection.Send(new MoveIntent(4, 90, 0.6f, 0.8f));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Input));
        Assert.That(sent.Delivery, Is.EqualTo(MessageDelivery.UnreliableSequenced));
        Assert.That(MoveInput.TryRead(sent.Payload, out MoveInput input), Is.True);
        Assert.That(input.Intent, Is.EqualTo(new MoveIntent(4, 90, 0.6f, 0.8f)));
    }

    [Test]
    public void Send_ZeroIntent_GoesOutAsStopMovementInTheSameSequenceSpace()
    {
        Harness harness = new Harness();
        harness.EnterWorld();

        harness.Connection.Send(new MoveIntent(5, 91, 0f, 0f));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Input));
        Assert.That(StopMovement.TryRead(sent.Payload, out StopMovement stop), Is.True);
        Assert.That(stop.Sequence, Is.EqualTo(5u));
        Assert.That(stop.ClientTick, Is.EqualTo(91u));
    }

    [Test]
    public void Send_BeforeTheWorldIsEntered_SendsNothing()
    {
        Harness harness = new Harness();
        harness.ConnectAndReceiveHello();
        int sentBefore = harness.Transport.Sent.Count;

        harness.Connection.Send(new MoveIntent(1, 1, 1f, 0f));

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(sentBefore));
    }

    [Test]
    public void Payload_OnTheWrongChannel_IsCountedAndIgnored()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        EntitySnapshot snapshot = ClientWorldFixture.Snapshot(
            5,
            0,
            ClientWorldFixture.State(ClientWorldFixture.LocalEntity, ClientTestGrids.Center(9, 8)));

        harness.Deliver(ProtocolChannel.Control, Encode(snapshot.GetEncodedLength(), snapshot.Write));

        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.World!.Predictor.Position, Is.EqualTo(Start));
    }

    [Test]
    public void Payload_ThatIsGarbageTruncatedOrClientBound_IsCountedAndIgnored()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        MoveInput echoed = new MoveInput(new MoveIntent(1, 1, 1f, 0f));
        EntitySnapshot snapshot = ClientWorldFixture.Snapshot(
            5,
            0,
            ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start));
        byte[] truncated = Encode(snapshot.GetEncodedLength(), snapshot.Write);
        Array.Resize(ref truncated, truncated.Length - 3);

        harness.Deliver(ProtocolChannel.Control, new byte[] { 0xFF, 0xFF, 0x00 });
        harness.Deliver(ProtocolChannel.Control, new byte[] { 0x01 });
        harness.Deliver(ProtocolChannel.Input, Encode(MoveInput.EncodedLength, echoed.Write));
        harness.Deliver(ProtocolChannel.State, truncated);

        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(4));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
    }

    [Test]
    public void ServerHello_WithAZeroTickRate_IsRejected()
    {
        Harness harness = new Harness();
        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();
        ServerHello hello = new ServerHello(ProtocolConstants.ProtocolVersion, "0.2.0-dev", 1, 0, 0);

        harness.Deliver(ProtocolChannel.Control, Encode(hello.GetEncodedLength(), hello.Write));

        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.AwaitingHello));
    }

    [Test]
    public void WorldEntered_BeforeTheHello_IsUnexpectedAndIgnored()
    {
        Harness harness = new Harness();
        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();
        WorldEntered entered = ClientWorldFixture.Entered(Start);

        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        Assert.That(harness.Connection.UnexpectedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.World, Is.Null);
    }

    [Test]
    public void Disconnect_WithANotice_KeepsTheReasonAndTheSafeMessage()
    {
        Harness harness = new Harness();
        harness.EnterWorld();
        DisconnectNotice notice = new DisconnectNotice(DisconnectReason.Maintenance, "Server is shutting down.");

        harness.Transport.DropConnection(
            TransportDisconnectCause.ClosedByServer,
            Encode(notice.GetEncodedLength(), notice.Write));
        harness.Connection.Poll();

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.DisconnectCause, Is.EqualTo(TransportDisconnectCause.ClosedByServer));
        Assert.That(harness.Connection.Notice!.Reason, Is.EqualTo(DisconnectReason.Maintenance));
        Assert.That(harness.Connection.Notice.Message, Is.EqualTo("Server is shutting down."));
        Assert.That(harness.ClosedCount, Is.EqualTo(1));
    }

    [Test]
    public void Disconnect_WithoutANotice_ReportsTheTransportCause()
    {
        Harness harness = new Harness();
        harness.EnterWorld();

        harness.Transport.DropConnection(TransportDisconnectCause.TimedOut, new byte[0]);
        harness.Connection.Poll();

        Assert.That(harness.Connection.Notice, Is.Null);
        Assert.That(harness.Connection.DisconnectCause, Is.EqualTo(TransportDisconnectCause.TimedOut));
    }

    [Test]
    public void Connect_WithAnInvalidContentVersion_FailsBeforeTouchingTheNetwork()
    {
        Harness harness = new Harness("not-a-version");

        harness.Connection.Connect("127.0.0.1", 7777);

        Assert.That(harness.Transport.Host, Is.Empty);
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.LocalError, Is.Not.Empty);
        Assert.That(harness.ClosedCount, Is.EqualTo(1));
    }

    [Test]
    public void Connect_WhenTheTransportCannotStart_FailsCleanlyAndCanBeRetried()
    {
        Harness harness = new Harness();
        harness.Transport.ThrowOnConnect = true;

        harness.Connection.Connect("127.0.0.1", 7777);

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.LocalError, Does.Contain("could not be started"));
        Assert.That(harness.ClosedCount, Is.EqualTo(1));

        harness.Transport.ThrowOnConnect = false;
        harness.Connection.Connect("127.0.0.1", 7777);

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Connecting));
        Assert.That(harness.Connection.LocalError, Is.Empty);
    }

    [Test]
    public void Disconnect_ForgetsTheWorld()
    {
        Harness harness = new Harness();
        harness.EnterWorld();

        harness.Transport.DropConnection(TransportDisconnectCause.TimedOut, new byte[0]);
        harness.Connection.Poll();

        Assert.That(harness.Connection.World, Is.Null, "a closed connection must not look like a live world");
    }

    [Test]
    public void LocalError_NeverContainsTheSessionToken()
    {
        Harness missingMap = new Harness { HasMap = false };
        missingMap.EnterWorld();
        Harness badVersion = new Harness("not-a-version");
        badVersion.Connection.Connect("127.0.0.1", 7777);

        Assert.That(missingMap.Connection.LocalError, Does.Not.Contain("tester-secret"));
        Assert.That(badVersion.Connection.LocalError, Does.Not.Contain("tester-secret"));
    }

    private delegate int Writer(Span<byte> destination);

    private static byte[] Encode(int length, Writer write)
    {
        byte[] payload = new byte[length];
        Assert.That(write(payload), Is.EqualTo(length));
        return payload;
    }

    private sealed class Harness : IMapProvider
    {
        public Harness(string contentVersion = ContentVersion)
        {
            Transport = new FakeClientTransport();
            Connection = new ClientConnection(
                Transport,
                new ClientConnectionSettings("0.2.0-dev", contentVersion, Token, new CharacterId(7)),
                this);
            Connection.Closed += () => ClosedCount++;
        }

        public FakeClientTransport Transport { get; }

        public ClientConnection Connection { get; }

        public bool HasMap { get; set; } = true;

        public int ClosedCount { get; private set; }

        public bool TryGetNavigation(MapDefinitionId map, out NavigationGrid? grid)
        {
            grid = HasMap ? ClientTestGrids.CreateYard() : null;
            return HasMap;
        }

        public void ConnectAndReceiveHello()
        {
            Connection.Connect("127.0.0.1", 7777);
            Transport.CompleteConnect();
            Connection.Poll();
            ServerHello hello = new ServerHello(ProtocolConstants.ProtocolVersion, "0.2.0-dev", 0x11326bd1u, 20, 0);
            Deliver(ProtocolChannel.Control, Encode(hello.GetEncodedLength(), hello.Write));
        }

        public void EnterWorld()
        {
            ConnectAndReceiveHello();
            WorldEntered entered = ClientWorldFixture.Entered(Start);
            Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));
            Connection.Poll();
        }

        public void Deliver(ProtocolChannel channel, byte[] payload)
        {
            Transport.Deliver(channel, payload);
            Connection.Poll();
        }
    }
}
}
