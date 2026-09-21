using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class WorldEntryTests
{
    [Test]
    public void EnterWorld_AfterHello_SpawnsPlayerAtMapSpawnPointAndSendsWorldEntered()
    {
        TestServer server = new TestServer();
        MapDefinition map = server.Content.Maps.Values.Single();

        ConnectionId connection = server.EnterWorld(7);

        InMemoryServerTransport.SentMessage sent = server.Transport.SentTo(connection).Last();
        Assert.That(WorldEntered.TryRead(sent.Payload, out WorldEntered? entered), Is.True);
        Assert.That(sent.Channel, Is.EqualTo(ProtocolChannel.Control));
        Assert.That(entered!.Map, Is.EqualTo(map.Id));
        Assert.That(entered.MapInstance, Is.EqualTo(1u));
        Assert.That(entered.ServerTick, Is.EqualTo(server.CurrentTick));
        Assert.That(entered.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(entered.Facing, Is.EqualTo(new WorldDirection(0f, 1f)));

        PlayerEntity player = server.PlayerOf(connection);
        Assert.That(entered.LocalEntity, Is.EqualTo(player.Id));
        Assert.That(player.Character, Is.EqualTo(new CharacterId(7)));
        Assert.That(player.Owner, Is.EqualTo(connection));
        Assert.That(player.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(server.World.Maps.Single().Players, Is.EquivalentTo(new[] { player }));
    }

    [Test]
    public void EnterWorld_MovementSpeed_ComesFromTheJobThroughTheRules()
    {
        TestServer server = new TestServer();
        JobDefinition job = server.Content.Jobs.Values.Single();

        ConnectionId connection = server.EnterWorld(7);

        WorldEntered.TryRead(server.Transport.SentTo(connection).Last().Payload, out WorldEntered? entered);
        Assert.That(entered!.MovementSpeed, Is.EqualTo((float)job.BaseSpeed));
        Assert.That(server.PlayerOf(connection).MovementSpeed, Is.EqualTo((float)job.BaseSpeed));
        Assert.That(server.PlayerOf(connection).Job, Is.EqualTo(job.Id));
    }

    [Test]
    public void EnterWorld_ForEachPlayer_AllocatesADistinctEntityId()
    {
        TestServer server = new TestServer();

        EntityId first = server.PlayerOf(server.EnterWorld(1)).Id;
        EntityId second = server.PlayerOf(server.EnterWorld(2)).Id;

        Assert.That(first.Value, Is.GreaterThan(0));
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void EnterWorld_BeforeHello_IsRejected()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendEnterWorld(connection, 7);

        server.Tick();

        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(DisconnectReason.AuthenticationFailed));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(long.MinValue)]
    public void EnterWorld_WithCharacterIdThatIsNotPositive_IsIgnored(long character)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);
        server.SendEnterWorld(connection, character);

        server.Tick();

        Assert.That(server.Transport.OpcodesSentTo(connection), Is.EqualTo(new[] { MessageOpcode.ServerHello }));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
    }

    [Test]
    public void EnterWorld_Twice_IsIgnored()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        EntityId entity = server.PlayerOf(connection).Id;
        server.SendEnterWorld(connection, 8);

        server.Tick();

        Assert.That(server.PlayerOf(connection).Id, Is.EqualTo(entity));
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));
        Assert.That(
            server.Transport.OpcodesSentTo(connection),
            Is.EqualTo(new[] { MessageOpcode.ServerHello, MessageOpcode.WorldEntered }));
    }

    [Test]
    public void EnterWorld_WithCharacterAlreadyInWorld_ReplacesOlderSession()
    {
        TestServer server = new TestServer();
        ConnectionId older = server.EnterWorld(7);
        EntityId olderEntity = server.PlayerOf(older).Id;

        ConnectionId newer = server.EnterWorld(7);

        Assert.That(server.Transport.Disconnects[older], Is.EqualTo(DisconnectReason.SessionReplaced));
        Assert.That(server.Sessions.TryGet(older, out _), Is.False);
        Assert.That(server.World.Maps.Single().Players.Select(player => player.Owner), Is.EqualTo(new[] { newer }));
        Assert.That(server.PlayerOf(newer).Id, Is.Not.EqualTo(olderEntity));
    }

    [Test]
    public void EnterWorld_AfterReplacedSessionsLateDisconnect_KeepsTheNewSession()
    {
        TestServer server = new TestServer();
        ConnectionId older = server.EnterWorld(7);
        ConnectionId newer = server.EnterWorld(7);
        server.Disconnect(older);

        server.Tick();

        Assert.That(server.Sessions.TryGet(newer, out _), Is.True);
        Assert.That(server.Sessions.TryGetByCharacter(new CharacterId(7), out ClientSession? bound), Is.True);
        Assert.That(bound!.Connection, Is.EqualTo(newer));
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));
    }

    [Test]
    public void Disconnect_OfPlayer_RemovesItsEntityAndFreesTheCharacter()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Disconnect(connection);

        server.Tick();

        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Sessions.Sessions, Is.Empty);
        Assert.That(server.Sessions.TryGetByCharacter(new CharacterId(7), out _), Is.False);
    }
}
}
