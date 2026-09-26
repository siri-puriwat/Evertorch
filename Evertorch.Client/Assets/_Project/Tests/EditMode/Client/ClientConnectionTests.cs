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
                new ClientConnectionSettings(ProtocolConstants.BuildVersion, contentVersion, Token),
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
            var hello = new ServerHello(
                ProtocolConstants.ProtocolVersion,
                ProtocolConstants.BuildVersion,
                0x11326bd1u,
                20,
                0);
            Deliver(ProtocolChannel.Control, Encode(hello.GetEncodedLength(), hello.Write));
        }

        public void ReceiveList(params CharacterListEntry[] characters)
        {
            var list = new CharacterList(characters);
            Deliver(ProtocolChannel.Control, Encode(list.GetEncodedLength(), list.Write));
        }

        public void EnterWorld(uint lastCommandSequence = 0)
        {
            ConnectAndReceiveHello();
            ReceiveList(Entry(7, "Ann0"));
            Connection.EnterWorld(new CharacterId(7));
            WorldEntered entered = ClientWorldFixture.Entered(Start, lastCommandSequence: lastCommandSequence);
            Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));
            Connection.Poll();
        }

        public void Deliver(ProtocolChannel channel, byte[] payload)
        {
            Transport.Deliver(channel, payload);
            Connection.Poll();
        }
    }

    private static CharacterListEntry Entry(long character, string name)
    {
        return new CharacterListEntry(new CharacterId(character), name, new JobDefinitionId("job.adventurer"), 1);
    }

    // The client has numbered two commands, 6 and 7. The server may report fewer, when the second was still on its way
    // as it moved the character, or more; the next command goes on after the later of the two, never 7 again.
    [TestCase(6u, 8u)]
    [TestCase(40u, 41u)]
    public void WorldEntered_InTheWorld_NumbersTheNextCommandAfterTheLaterSequence(uint reported, uint expected)
    {
        var harness = new Harness();
        harness.EnterWorld(5);
        harness.Connection.SendAttack(new EntityId(300));
        harness.Connection.SendCancel();
        WorldEntered field = ClientWorldFixture.Entered(
            Start,
            lastCommandSequence: reported,
            mapEpoch: 1,
            map: "map.training_field");

        harness.Deliver(ProtocolChannel.Control, Encode(field.GetEncodedLength(), field.Write));
        uint next = harness.Connection.SendAttack(new EntityId(301));

        Assert.That(next, Is.EqualTo(expected));
    }

    [Test]
    public void CharacterList_AfterALogout_IsNotTakenForTheAnswerToAnEarlierCreation()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList();
        harness.Connection.CreateCharacter("Ann0");
        var result = new CreateCharacterResult(CreateCharacterOutcome.Created, new CharacterId(7));
        harness.Deliver(ProtocolChannel.Control, Encode(CreateCharacterResult.EncodedLength, result.Write));
        harness.ReceiveList(Entry(7, "Ann0"));
        harness.Connection.EnterWorld(new CharacterId(7));
        WorldEntered entered = ClientWorldFixture.Entered(Start);
        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        harness.Deliver(ProtocolChannel.Control, Encode(LogoutComplete.EncodedLength, new LogoutComplete().Write));
        harness.ReceiveList(Entry(7, "Ann0"));

        Assert.That(harness.Connection.LastCreateOutcome, Is.EqualTo(CreateCharacterOutcome.None));
        Assert.That(
            GameClient.CharacterListStatus(harness.Connection.State, harness.Connection.LastCreateOutcome),
            Is.EqualTo("Choose or create a character"));
    }

    [Test]
    public void CharacterList_BeforeTheHello_IsUnexpected()
    {
        var harness = new Harness();
        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();

        harness.ReceiveList(Entry(7, "Ann0"));

        Assert.That(harness.Connection.UnexpectedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.Characters, Is.Empty);
    }

    [Test]
    public void CharacterList_ThatOnlyCrossedAnEntryRequest_KeepsTheStatusLine()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"));
        harness.Connection.EnterWorld(new CharacterId(7));

        harness.ReceiveList(Entry(7, "Ann0"));

        Assert.That(
            GameClient.CharacterListStatus(harness.Connection.State, harness.Connection.LastCreateOutcome),
            Is.Null);
    }

    [Test]
    public void CharacterList_WhileSelecting_IsKeptAndAnnounced()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int announced = 0;
        harness.Connection.CharactersChanged += () => announced++;

        harness.ReceiveList(Entry(7, "Ann0"), Entry(9, "Bob12"));

        Assert.That(announced, Is.EqualTo(1));
        Assert.That(harness.Connection.Characters.Select(entry => entry.Name), Is.EqualTo(new[] { "Ann0", "Bob12" }));
    }

    [Test]
    public void CharacterProgress_InTheWorld_ReachesTheWorld()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var progress = new CharacterProgress(2, 5, 50);

        harness.Deliver(ProtocolChannel.Control, Encode(CharacterProgress.EncodedLength, progress.Write));
        harness.Deliver(ProtocolChannel.Control, new byte[] { 0x1A, 0x80, 0x00 });

        Assert.That(harness.Connection.World!.Level, Is.EqualTo(2));
        Assert.That(harness.Connection.World.Experience, Is.EqualTo(5UL));
        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(1), "a truncated progress");
    }

    [Test]
    public void CommandRejected_InTheWorld_ReachesTheWorldWithItsSequence()
    {
        var harness = new Harness();
        harness.EnterWorld();
        uint sequence = harness.Connection.SendAttack(new EntityId(300));
        var rejections = new List<CommandRejected>();
        harness.Connection.World!.CommandRejectedReceived += rejections.Add;
        var rejected = new CommandRejected(sequence, CommandRejectionReason.InvalidTarget);

        harness.Deliver(ProtocolChannel.Control, Encode(CommandRejected.EncodedLength, rejected.Write));

        Assert.That(sequence, Is.EqualTo(1u));
        Assert.That(rejections.Count, Is.EqualTo(1));
        Assert.That(rejections[0].CommandSequence, Is.EqualTo(1u));
        Assert.That(harness.Connection.World.LastRejection, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Commands_AfterEnteringACharacterThatUsedSequences_ContinueAfterTheReportedOne()
    {
        var harness = new Harness();
        harness.EnterWorld(41);
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendAttack(new EntityId(300));
        harness.Connection.SendCancel();

        FakeClientTransport.SentMessage[] sent = harness.Transport.Sent.Skip(before).ToArray();
        Assert.That(AttackEntity.TryRead(sent[0].Payload, out AttackEntity attack), Is.True);
        Assert.That(CancelAction.TryRead(sent[1].Payload, out CancelAction cancel), Is.True);
        Assert.That(attack.CommandSequence, Is.EqualTo(42u));
        Assert.That(cancel.CommandSequence, Is.EqualTo(43u));
    }

    [Test]
    public void Commands_BeforeTheWorldIsEntered_SendNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendAttack(new EntityId(300));
        harness.Connection.SendCancel();
        harness.Connection.SendRespawn();

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void Commands_ShareOneSequenceStartingAtOne_OnTheReliableChannel()
    {
        var harness = new Harness();
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
    public void Connect_ThenConnected_SendsTheHelloOnTheControlChannel()
    {
        var harness = new Harness();

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
        Assert.That(hello.ClientBuildVersion, Is.EqualTo(ProtocolConstants.BuildVersion));
        Assert.That(hello.ClientContentVersion, Is.EqualTo(0x11326bd1u));
        Assert.That(hello.SessionToken, Is.EqualTo(Token));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.AwaitingHello));
    }

    [Test]
    public void Connect_WhenTheTransportCannotStart_FailsCleanlyAndCanBeRetried()
    {
        var harness = new Harness();
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
    public void Connect_WithAnInvalidContentVersion_FailsBeforeTouchingTheNetwork()
    {
        var harness = new Harness("not-a-version");

        harness.Connection.Connect("127.0.0.1", 7777);

        Assert.That(harness.Transport.Host, Is.Empty);
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.LocalError, Is.Not.Empty);
        Assert.That(harness.ClosedCount, Is.EqualTo(1));
    }

    [Test]
    public void CreateCharacter_RefusedWhileAnEntryIsUnanswered_SaysWhyOnTheStatusLine()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"));
        harness.Connection.EnterWorld(new CharacterId(7));

        harness.Connection.CreateCharacter("ab");
        var result = new CreateCharacterResult(CreateCharacterOutcome.NameInvalid, default);
        harness.Deliver(ProtocolChannel.Control, Encode(CreateCharacterResult.EncodedLength, result.Write));
        harness.ReceiveList(Entry(7, "Ann0"));

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.EnteringWorld));
        Assert.That(
            GameClient.CharacterListStatus(harness.Connection.State, harness.Connection.LastCreateOutcome),
            Does.StartWith("Name refused"));
    }

    [Test]
    public void CreateCharacter_WhileSelecting_SendsTheNameAndKeepsTheAnswer()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList();

        bool isSent = harness.Connection.CreateCharacter("Ann0");
        var result = new CreateCharacterResult(CreateCharacterOutcome.NameTaken, default);
        harness.Deliver(ProtocolChannel.Control, Encode(CreateCharacterResult.EncodedLength, result.Write));

        Assert.That(isSent, Is.True);
        Assert.That(CreateCharacter.TryRead(harness.Transport.Sent.Last().Payload, out CreateCharacter? sent), Is.True);
        Assert.That(sent!.Name, Is.EqualTo("Ann0"));
        Assert.That(harness.Connection.LastCreateOutcome, Is.EqualTo(CreateCharacterOutcome.NameTaken));
    }

    [Test]
    public void CreateCharacter_WithANameTooLongForTheWire_SendsNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        bool isSent = harness.Connection.CreateCharacter("Abcdefghijklmnopqrstuvwx");

        Assert.That(isSent, Is.False);
        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void Disconnect_ForgetsTheWorld()
    {
        var harness = new Harness();
        harness.EnterWorld();

        harness.Transport.DropConnection(TransportDisconnectCause.TimedOut, new byte[0]);
        harness.Connection.Poll();

        Assert.That(harness.Connection.World, Is.Null, "a closed connection must not look like a live world");
    }

    [Test]
    public void Disconnect_WithANotice_KeepsTheReasonAndTheSafeMessage()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var notice = new DisconnectNotice(DisconnectReason.Maintenance, "Server is shutting down.");

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
        var harness = new Harness();
        harness.EnterWorld();

        harness.Transport.DropConnection(TransportDisconnectCause.TimedOut, new byte[0]);
        harness.Connection.Poll();

        Assert.That(harness.Connection.Notice, Is.Null);
        Assert.That(harness.Connection.DisconnectCause, Is.EqualTo(TransportDisconnectCause.TimedOut));
    }

    [Test]
    public void EnterWorld_ForAListedCharacter_SendsTheRequestAndWaitsForTheWorld()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"));

        bool isSent = harness.Connection.EnterWorld(new CharacterId(7));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(isSent, Is.True);
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(EnterWorldRequest.TryRead(sent.Payload, out EnterWorldRequest request), Is.True);
        Assert.That(request.Character, Is.EqualTo(new CharacterId(7)));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.EnteringWorld));
    }

    [Test]
    public void EnterWorld_TwiceDuringOneLoad_TheWorldNamesTheCharacterTheServerEntered()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"), Entry(9, "Bob12"));
        harness.Connection.EnterWorld(new CharacterId(7));
        harness.Connection.EnterWorld(new CharacterId(9));

        WorldEntered entered = ClientWorldFixture.Entered(Start, character: 7);
        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        Assert.That(harness.Connection.World!.Character, Is.EqualTo(new CharacterId(7)),
            "the second request is not the one the server answered");
    }

    [Test]
    public void EnterWorld_WhileAnEarlierEntryIsUnanswered_CanChooseAgainAndStillEnter()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"), Entry(9, "Bob12"));
        harness.Connection.EnterWorld(new CharacterId(7));

        bool isSentAgain = harness.Connection.EnterWorld(new CharacterId(9));
        bool isCreateSent = harness.Connection.CreateCharacter("Cid3");
        WorldEntered entered = ClientWorldFixture.Entered(Start);
        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        Assert.That(isSentAgain, Is.True, "a refused entry is not answered, so the list stays usable");
        Assert.That(isCreateSent, Is.True);
        Assert.That(EnterWorldRequest.TryRead(harness.Transport.Sent[harness.Transport.Sent.Count - 2].Payload,
            out EnterWorldRequest again), Is.True);
        Assert.That(again.Character, Is.EqualTo(new CharacterId(9)));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
    }

    [Test]
    public void InventoryChanged_WithARevisionGap_SendsOneResyncRequest()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var part = new InventorySnapshot(4, 0, 1, Array.Empty<InventoryEntry>());
        harness.Deliver(ProtocolChannel.Control, Encode(part.GetEncodedLength(), part.Write));
        int before = harness.Transport.Sent.Count;
        var gel = new ItemDefinitionId("item.material.slime_gel");
        var skipped = new InventoryChanged(5, 6, new[] { new InventoryEntry(11, gel, 1) });
        var next = new InventoryChanged(6, 7, new[] { new InventoryEntry(11, gel, 2) });

        harness.Deliver(ProtocolChannel.Control, Encode(skipped.GetEncodedLength(), skipped.Write));
        harness.Deliver(ProtocolChannel.Control, Encode(next.GetEncodedLength(), next.Write));

        FakeClientTransport.SentMessage[] sent = harness.Transport.Sent.Skip(before).ToArray();
        Assert.That(sent.Length, Is.EqualTo(1));
        Assert.That(InventoryResyncRequest.TryRead(sent[0].Payload, out _), Is.True);
        Assert.That(sent[0].Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(sent[0].Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(harness.Connection.World!.Inventory.Rows, Is.Empty);
    }

    [Test]
    public void InventorySnapshot_InTheWorld_ReachesTheWorldsInventory()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var part = new InventorySnapshot(
            4,
            0,
            1,
            new[] { new InventoryEntry(11, new ItemDefinitionId("item.material.slime_gel"), 3) });

        harness.Deliver(ProtocolChannel.Control, Encode(part.GetEncodedLength(), part.Write));

        Assert.That(harness.Connection.World!.Inventory.IsCurrent, Is.True);
        Assert.That(harness.Connection.World.Inventory.Revision, Is.EqualTo(4u));
        Assert.That(harness.Connection.World.Inventory.Rows.Single().Quantity, Is.EqualTo(3u));
    }

    [Test]
    public void InventorySnapshot_OutsideTheWorld_IsUnexpected()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        var part = new InventorySnapshot(4, 0, 1, Array.Empty<InventoryEntry>());

        harness.Deliver(ProtocolChannel.Control, Encode(part.GetEncodedLength(), part.Write));

        Assert.That(harness.Connection.UnexpectedMessages, Is.EqualTo(1));
    }

    [Test]
    public void ItemPickedUp_ForAKnownDrop_ReachesTheWorld()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var spawn = new EntitySpawn(
            new EntityId(400),
            EntityKind.ItemDrop,
            "item.material.slime_gel",
            Start,
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0);
        harness.Deliver(ProtocolChannel.Control, Encode(spawn.GetEncodedLength(), spawn.Write));
        var received = new List<ItemPickedUp>();
        harness.Connection.World!.ItemPickedUpReceived += received.Add;
        var pickedUp = new ItemPickedUp(
            new EntityId(400),
            ClientWorldFixture.LocalEntity,
            new ItemDefinitionId("item.material.slime_gel"),
            2);

        harness.Deliver(ProtocolChannel.Control, Encode(pickedUp.GetEncodedLength(), pickedUp.Write));

        Assert.That(received.Count, Is.EqualTo(1));
        Assert.That(received[0].Amount, Is.EqualTo(2u));
    }

    [Test]
    public void LocalError_NeverContainsTheSessionToken()
    {
        var missingMap = new Harness { HasMap = false };
        missingMap.EnterWorld();
        var badVersion = new Harness("not-a-version");
        badVersion.Connection.Connect("127.0.0.1", 7777);

        Assert.That(missingMap.Connection.LocalError, Does.Not.Contain("tester-secret"));
        Assert.That(badVersion.Connection.LocalError, Does.Not.Contain("tester-secret"));
    }

    [Test]
    public void LogoutComplete_LeavesTheWorldForCharacterSelection()
    {
        var harness = new Harness();
        harness.EnterWorld();
        int left = 0;
        harness.Connection.LeftWorld += () => left++;
        harness.Connection.SendLogout();

        harness.Deliver(ProtocolChannel.Control, Encode(LogoutComplete.EncodedLength, new LogoutComplete().Write));
        harness.ReceiveList(Entry(7, "Ann0"));

        Assert.That(left, Is.EqualTo(1));
        Assert.That(harness.Connection.World, Is.Null);
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.SelectingCharacter));
        Assert.That(harness.Connection.Characters.Count, Is.EqualTo(1));
        Assert.That(harness.ClosedCount, Is.Zero, "the connection stays open");
    }

    [Test]
    public void LogoutComplete_OutsideTheWorld_IsUnexpected()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();

        harness.Deliver(ProtocolChannel.Control, Encode(LogoutComplete.EncodedLength, new LogoutComplete().Write));

        Assert.That(harness.Connection.UnexpectedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.SelectingCharacter));
    }

    [Test]
    public void Payload_OnTheWrongChannel_IsCountedAndIgnored()
    {
        var harness = new Harness();
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
        var harness = new Harness();
        harness.EnterWorld();
        var echoed = new MoveInput(new MoveIntent(1, 1, 1f, 0f));
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
    public void SelectionRequests_OutsideSelection_SendNothing()
    {
        var harness = new Harness();
        harness.EnterWorld();
        int before = harness.Transport.Sent.Count;

        bool isCreated = harness.Connection.CreateCharacter("Ann0");
        bool isEntered = harness.Connection.EnterWorld(new CharacterId(7));

        Assert.That(new[] { isCreated, isEntered }, Is.All.False);
        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void SendEquipAndUnequip_InTheWorld_ShareTheCommandSequence()
    {
        var harness = new Harness();
        harness.EnterWorld(4);
        harness.Connection.SendAttack(new EntityId(300));

        uint equipSequence = harness.Connection.SendEquip(41);
        FakeClientTransport.SentMessage equipSent = harness.Transport.Sent.Last();
        uint unequipSequence = harness.Connection.SendUnequip(EquipmentSlot.Armor);
        FakeClientTransport.SentMessage unequipSent = harness.Transport.Sent.Last();

        Assert.That((equipSequence, unequipSequence), Is.EqualTo((6u, 7u)));
        Assert.That(EquipItem.TryRead(equipSent.Payload, out EquipItem equip), Is.True);
        Assert.That((equip.InventoryItem, equip.CommandSequence), Is.EqualTo((41L, 6u)));
        Assert.That(UnequipItem.TryRead(unequipSent.Payload, out UnequipItem unequip), Is.True);
        Assert.That((unequip.Slot, unequip.CommandSequence), Is.EqualTo((EquipmentSlot.Armor, 7u)));
        Assert.That(
            new[] { equipSent.Channel, unequipSent.Channel },
            Is.All.EqualTo(ProtocolChannel.Control));
    }

    [Test]
    public void SendEquipAndUnequip_OutsideTheWorld_SendNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        uint equip = harness.Connection.SendEquip(41);
        uint unequip = harness.Connection.SendUnequip(EquipmentSlot.Weapon);

        Assert.That((equip, unequip), Is.EqualTo((0u, 0u)));
        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void SendLogout_InTheWorld_UsesTheCommandSequenceAndKeepsTheWorldUntilConfirmed()
    {
        var harness = new Harness();
        harness.EnterWorld();
        harness.Connection.SendAttack(new EntityId(300));

        harness.Connection.SendLogout();

        Assert.That(Logout.TryRead(harness.Transport.Sent.Last().Payload, out Logout logout), Is.True);
        Assert.That(logout.CommandSequence, Is.EqualTo(2u));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
        Assert.That(harness.Connection.World, Is.Not.Null);
    }

    [Test]
    public void SendPickup_InTheWorld_SharesTheCommandSequenceWithAttacks()
    {
        var harness = new Harness();
        harness.EnterWorld(4);
        harness.Connection.SendAttack(new EntityId(300));

        uint sequence = harness.Connection.SendPickup(new EntityId(400));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sequence, Is.EqualTo(6u));
        Assert.That(PickupItem.TryRead(sent.Payload, out PickupItem pickup), Is.True);
        Assert.That(pickup.Drop, Is.EqualTo(new EntityId(400)));
        Assert.That(pickup.CommandSequence, Is.EqualTo(6u));
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
    }

    [Test]
    public void SendPickup_OutsideTheWorld_SendsNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        uint sequence = harness.Connection.SendPickup(new EntityId(400));

        Assert.That(sequence, Is.Zero);
        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void SendTarget_BeforeTheWorldIsEntered_SendsNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int before = harness.Transport.Sent.Count;

        harness.Connection.SendTarget(new EntityId(300));

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(before));
    }

    [Test]
    public void SendUseSkill_InTheWorld_SharesTheCommandSequenceOnTheReliableChannel()
    {
        var harness = new Harness();
        harness.EnterWorld();
        uint attack = harness.Connection.SendAttack(new EntityId(300));

        uint skill = harness.Connection.SendUseSkill(new SkillDefinitionId("skill.strike"), new EntityId(300));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(UseSkill.TryRead(sent.Payload, out UseSkill? read), Is.True);
        Assert.That((attack, skill), Is.EqualTo((1u, 2u)));
        Assert.That(read!.CommandSequence, Is.EqualTo(2u));
        Assert.That(read.Skill, Is.EqualTo(new SkillDefinitionId("skill.strike")));
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
    }

    [Test]
    public void Send_BeforeTheWorldIsEntered_SendsNothing()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        int sentBefore = harness.Transport.Sent.Count;

        harness.Connection.Send(new MoveIntent(1, 1, 1f, 0f));

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(sentBefore));
    }

    [Test]
    public void Send_Movement_EchoesTheMapEpochOfTheWorldEntered()
    {
        var harness = new Harness();
        harness.ConnectAndReceiveHello();
        harness.ReceiveList(Entry(7, "Ann0"));
        harness.Connection.EnterWorld(new CharacterId(7));
        WorldEntered entered = ClientWorldFixture.Entered(Start, mapEpoch: 3);
        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        harness.Connection.Send(new MoveIntent(1, 1, 1f, 0f));
        FakeClientTransport.SentMessage move = harness.Transport.Sent.Last();
        harness.Connection.Send(new MoveIntent(2, 2, 0f, 0f));
        FakeClientTransport.SentMessage stop = harness.Transport.Sent.Last();

        Assert.That(MoveInput.TryRead(move.Payload, out MoveInput moveRead), Is.True);
        Assert.That(StopMovement.TryRead(stop.Payload, out StopMovement stopRead), Is.True);
        Assert.That((moveRead.MapEpoch, stopRead.MapEpoch), Is.EqualTo(((byte)3, (byte)3)));
        Assert.That(harness.Connection.MapEpoch, Is.EqualTo(3));
    }

    [Test]
    public void Send_MovingIntent_GoesOutAsMoveInputOnTheInputChannel()
    {
        var harness = new Harness();
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
        var harness = new Harness();
        harness.EnterWorld();

        harness.Connection.Send(new MoveIntent(5, 91, 0f, 0f));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Input));
        Assert.That(StopMovement.TryRead(sent.Payload, out StopMovement stop), Is.True);
        Assert.That(stop.Sequence, Is.EqualTo(5u));
        Assert.That(stop.ClientTick, Is.EqualTo(91u));
    }

    [Test]
    public void ServerHello_StartsCharacterSelectionWithoutEnteringAnything()
    {
        var harness = new Harness();
        int sentBefore;

        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();
        sentBefore = harness.Transport.Sent.Count;
        var hello = new ServerHello(
            ProtocolConstants.ProtocolVersion,
            ProtocolConstants.BuildVersion,
            0x11326bd1u,
            20,
            0);
        harness.Deliver(ProtocolChannel.Control, Encode(hello.GetEncodedLength(), hello.Write));

        Assert.That(harness.Transport.Sent.Count, Is.EqualTo(sentBefore));
        Assert.That(harness.Connection.ServerTickRate, Is.EqualTo(20u));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.SelectingCharacter));
    }

    [Test]
    public void ServerHello_WithAZeroTickRate_IsRejected()
    {
        var harness = new Harness();
        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();
        var hello = new ServerHello(ProtocolConstants.ProtocolVersion, ProtocolConstants.BuildVersion, 1, 0, 0);

        harness.Deliver(ProtocolChannel.Control, Encode(hello.GetEncodedLength(), hello.Write));

        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.AwaitingHello));
    }

    [Test]
    public void SkillMessages_InTheWorld_ReachTheWorld_AndACancelEndsTheCastLock()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var started = new SkillCastStarted(
            ClientWorldFixture.LocalEntity,
            new SkillDefinitionId("skill.first_aid"),
            default,
            10,
            1331);
        var list = new SkillList(
            new[] { new SkillListEntry(new SkillDefinitionId("skill.first_aid"), 0f, 3, 0, 0, 0) });
        var resolved = new SkillResolved(
            ClientWorldFixture.LocalEntity,
            ClientWorldFixture.LocalEntity,
            new SkillDefinitionId("skill.first_aid"),
            SkillOutcome.Healed,
            15,
            37,
            0);
        var heard = new List<SkillResolved>();
        harness.Connection.World!.SkillResolvedReceived += heard.Add;
        var effects = new StatusEffects(new[]
            { new StatusEffectEntry(new StatusDefinitionId("status.focus"), 60_000) });

        harness.Deliver(ProtocolChannel.Control, Encode(started.GetEncodedLength(), started.Write));
        harness.Deliver(ProtocolChannel.Control, Encode(list.GetEncodedLength(), list.Write));
        harness.Deliver(ProtocolChannel.Control, Encode(effects.GetEncodedLength(), effects.Write));
        bool isLocked = harness.Connection.World.ActionLock.IsCastLocked;
        harness.Connection.SendCancel();
        harness.Deliver(ProtocolChannel.Control, Encode(resolved.GetEncodedLength(), resolved.Write));

        Assert.That(isLocked, Is.True);
        Assert.That(harness.Connection.World.ActionLock.IsCastLocked, Is.False, "the player's own cancel");
        Assert.That(harness.Connection.World.Skills.Count, Is.EqualTo(1));
        Assert.That(harness.Connection.World.StatusEffects.Count, Is.EqualTo(1));
        Assert.That(heard, Has.Count.EqualTo(1));
        Assert.That(harness.Connection.MalformedMessages, Is.Zero);
    }

    [Test]
    public void SpawnDespawnAndSnapshot_ReachTheWorld()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var other = new EntityId(200);
        var spawn = new EntitySpawn(
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
        var despawn = new EntityDespawn(other, DespawnReason.OutOfRange);
        harness.Deliver(ProtocolChannel.Control, Encode(EntityDespawn.EncodedLength, despawn.Write));

        Assert.That(wasKnown, Is.True);
        Assert.That(states, Is.EqualTo(2));
        Assert.That(harness.Connection.World.Remotes.Count, Is.EqualTo(0));
        Assert.That(harness.Connection.MalformedMessages, Is.EqualTo(0));
    }

    [Test]
    public void TargetChanged_ReachesTheWorld_AndSendTargetGoesOutReliably()
    {
        var harness = new Harness();
        harness.EnterWorld();
        var slime = new EntityId(300);
        var spawn = new EntitySpawn(
            slime,
            EntityKind.Monster,
            "monster.training_slime",
            ClientTestGrids.Center(4, 8),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            1000);
        harness.Deliver(ProtocolChannel.Control, Encode(spawn.GetEncodedLength(), spawn.Write));

        harness.Connection.SendTarget(slime);
        var changed = new TargetChanged(ClientWorldFixture.LocalEntity, slime);
        harness.Deliver(ProtocolChannel.Control, Encode(TargetChanged.EncodedLength, changed.Write));

        FakeClientTransport.SentMessage sent = harness.Transport.Sent.Last();
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(sent.Delivery, Is.EqualTo(MessageDelivery.ReliableOrdered));
        Assert.That(TargetEntity.TryRead(sent.Payload, out TargetEntity request), Is.True);
        Assert.That(request.Target, Is.EqualTo(slime));
        Assert.That(harness.Connection.World!.Target, Is.EqualTo(slime));
    }

    [Test]
    public void WorldEntered_BeforeTheHello_IsUnexpectedAndIgnored()
    {
        var harness = new Harness();
        harness.Connection.Connect("127.0.0.1", 7777);
        harness.Transport.CompleteConnect();
        harness.Connection.Poll();
        WorldEntered entered = ClientWorldFixture.Entered(Start);

        harness.Deliver(ProtocolChannel.Control, Encode(entered.GetEncodedLength(), entered.Write));

        Assert.That(harness.Connection.UnexpectedMessages, Is.EqualTo(1));
        Assert.That(harness.Connection.World, Is.Null);
    }

    [Test]
    public void WorldEntered_CreatesTheWorldAndAnnouncesIt()
    {
        var harness = new Harness();
        var entered = new List<ClientWorld>();
        harness.Connection.EnteredWorld += entered.Add;

        harness.EnterWorld();

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
        Assert.That(entered.Single(), Is.SameAs(harness.Connection.World));
        Assert.That(harness.Connection.World!.Predictor.Position, Is.EqualTo(Start));
    }

    [Test]
    public void WorldEntered_ForAMapTheClientDoesNotHave_GivesUpWithAClearError()
    {
        var harness = new Harness { HasMap = false };

        harness.EnterWorld();

        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.Disconnected));
        Assert.That(harness.Connection.World, Is.Null);
        Assert.That(harness.Connection.LocalError, Does.Contain("map.training_ground"));
        Assert.That(harness.Transport.DisconnectCalls, Is.EqualTo(1));
        Assert.That(harness.ClosedCount, Is.EqualTo(1), "closing is announced once, not once per cause");
    }

    [Test]
    public void WorldEntered_InTheWorld_ChangesTheMapOnTheSameConnection()
    {
        var harness = new Harness();
        var entered = new List<ClientWorld>();
        var changed = new List<ClientWorld>();
        harness.Connection.EnteredWorld += entered.Add;
        harness.Connection.ChangedMap += changed.Add;
        harness.EnterWorld();
        ClientWorld ground = harness.Connection.World!;
        WorldPosition arrival = ClientTestGrids.Center(9, 1);
        WorldEntered field = ClientWorldFixture.Entered(arrival, mapEpoch: 1, map: "map.training_field");

        harness.Deliver(ProtocolChannel.Control, Encode(field.GetEncodedLength(), field.Write));
        harness.Connection.Send(new MoveIntent(9, 9, 1f, 0f));

        ClientWorld? world = harness.Connection.World;
        Assert.That(harness.Connection.State, Is.EqualTo(ClientConnectionState.InWorld));
        Assert.That((entered.Count, changed.Count), Is.EqualTo((1, 1)));
        Assert.That(changed.Single(), Is.SameAs(world));
        Assert.That(world, Is.Not.SameAs(ground));
        Assert.That(world!.Map, Is.EqualTo(new MapDefinitionId("map.training_field")));
        Assert.That(world.Predictor.Position, Is.EqualTo(arrival));
        Assert.That(MoveInput.TryRead(harness.Transport.Sent.Last().Payload, out MoveInput move), Is.True);
        Assert.That(move.MapEpoch, Is.EqualTo(1), "movement input carries the new map's epoch");
        Assert.That(harness.Connection.UnexpectedMessages, Is.Zero);
        Assert.That(harness.Transport.DisconnectCalls, Is.Zero);
    }
}
}
