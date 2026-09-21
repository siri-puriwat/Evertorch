using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
/// Interest cells are 4 m here and the neighbour radius is 1, so a player sees cells -1..1 around its own.
/// </summary>
[TestFixture]
public sealed class VisibilityTests
{
    [Test]
    public void WorldEntered_IsFollowedBySpawnsOfVisibleEntities_OnControlChannel()
    {
        TestServer server = CreateServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);

        MessageOpcode[] expected = { MessageOpcode.ServerHello, MessageOpcode.WorldEntered, MessageOpcode.EntitySpawn };
        Assert.That(server.Transport.OpcodesSentTo(second), Is.EqualTo(expected));
        Assert.That(server.Transport.SentTo(second).Select(message => message.Channel),
            Is.All.EqualTo(ProtocolChannel.Control));

        EntitySpawn.TryRead(server.Transport.SentTo(second).Last().Payload, out EntitySpawn? spawn);
        PlayerEntity firstPlayer = server.PlayerOf(first);
        Assert.That(spawn!.Entity, Is.EqualTo(firstPlayer.Id));
        Assert.That(spawn.Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(spawn.DefinitionId, Is.EqualTo(firstPlayer.Job.Value));
        Assert.That(spawn.Position, Is.EqualTo(firstPlayer.Position));
    }

    [Test]
    public void Visibility_WhenAnotherPlayerEnters_TellsTheExistingPlayerOnce()
    {
        TestServer server = CreateServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);

        server.Tick(5);

        MessageOpcode[] toFirst = server.Transport.OpcodesSentTo(first).ToArray();
        Assert.That(toFirst.Count(opcode => opcode == MessageOpcode.EntitySpawn), Is.EqualTo(1));
        EntitySpawn.TryRead(server.Transport.SentTo(first).Last().Payload, out EntitySpawn? spawn);
        Assert.That(spawn!.Entity, Is.EqualTo(server.PlayerOf(second).Id));
    }

    [Test]
    public void Visibility_NeverTellsAPlayerAboutItself()
    {
        TestServer server = CreateServer();
        ConnectionId only = server.EnterWorld(1);

        server.Tick(3);

        Assert.That(server.Transport.OpcodesSentTo(only), Has.None.EqualTo(MessageOpcode.EntitySpawn));
    }

    [Test]
    public void Visibility_ForDistantPlayer_SendsNothing()
    {
        TestServer server = CreateServer();
        ConnectionId near = server.EnterWorld(1);
        ConnectionId far = Enter(server, 2, 10f, 0.5f);

        server.Tick(3);

        Assert.That(server.Transport.OpcodesSentTo(near), Has.None.EqualTo(MessageOpcode.EntitySpawn));
        Assert.That(server.Transport.OpcodesSentTo(far), Has.None.EqualTo(MessageOpcode.EntitySpawn));
    }

    [Test]
    public void Visibility_WhenPlayerEntersNeighbourCell_SendsSpawnOnceToBoth()
    {
        TestServer server = CreateServer();
        ConnectionId near = Enter(server, 1, 0.5f, 0.5f);
        ConnectionId far = Enter(server, 2, 10f, 0.5f);
        server.Transport.ClearSent();

        server.Place(far, 6f, 0.5f);
        server.Tick(4);

        Assert.That(server.Transport.OpcodesSentTo(near), Is.EqualTo(new[] { MessageOpcode.EntitySpawn }));
        Assert.That(server.Transport.OpcodesSentTo(far), Is.EqualTo(new[] { MessageOpcode.EntitySpawn }));
    }

    [Test]
    public void Visibility_WhenPlayerLeavesInterestArea_SendsDespawnOutOfRange()
    {
        TestServer server = CreateServer();
        ConnectionId near = Enter(server, 1, 0.5f, 0.5f);
        ConnectionId mover = Enter(server, 2, 6f, 0.5f);
        server.Tick();
        server.Transport.ClearSent();

        server.Place(mover, 10f, 0.5f);
        server.Tick(2);

        InMemoryServerTransport.SentMessage sent = server.Transport.SentTo(near).Single();
        Assert.That(EntityDespawn.TryRead(sent.Payload, out EntityDespawn despawn), Is.True);
        Assert.That(despawn.Entity, Is.EqualTo(server.PlayerOf(mover).Id));
        Assert.That(despawn.Reason, Is.EqualTo(DespawnReason.OutOfRange));
        Assert.That(server.Transport.OpcodesSentTo(mover), Is.EqualTo(new[] { MessageOpcode.EntityDespawn }));
    }

    [Test]
    public void Disconnect_OfPlayer_DespawnsEntityForObserversAsRemoved()
    {
        TestServer server = CreateServer();
        ConnectionId observer = server.EnterWorld(1);
        ConnectionId leaver = server.EnterWorld(2);
        server.Tick();
        long leaverEntity = server.PlayerOf(leaver).Id.Value;
        server.Transport.ClearSent();

        server.Disconnect(leaver);
        server.Tick();

        InMemoryServerTransport.SentMessage sent = server.Transport.SentTo(observer).Single();
        EntityDespawn.TryRead(sent.Payload, out EntityDespawn despawn);
        Assert.That(despawn.Entity.Value, Is.EqualTo(leaverEntity));
        Assert.That(despawn.Reason, Is.EqualTo(DespawnReason.Removed));
    }

    [Test]
    public void Visibility_AfterReturningToTheInterestArea_SendsAFreshSpawn()
    {
        TestServer server = CreateServer();
        ConnectionId near = Enter(server, 1, 0.5f, 0.5f);
        ConnectionId mover = Enter(server, 2, 6f, 0.5f);
        server.Tick();
        server.Place(mover, 10f, 0.5f);
        server.Tick();
        server.Transport.ClearSent();

        server.Place(mover, 6f, 0.5f);
        server.Tick();

        Assert.That(server.Transport.OpcodesSentTo(near), Is.EqualTo(new[] { MessageOpcode.EntitySpawn }));
    }

    private static TestServer CreateServer()
    {
        return new TestServer(interestCellSize: 4f, interestNeighborRadius: 1);
    }

    private static ConnectionId Enter(TestServer server, long character, float x, float z)
    {
        ConnectionId connection = server.Connect();
        server.SendHello(connection);
        server.SendEnterWorld(connection, character);

        // Moved off the spawn point before the first visibility pass, so nobody ever sees it there.
        server.AfterCommandsOnce(() => server.Place(connection, x, z));
        server.Tick();
        return connection;
    }
}
}
