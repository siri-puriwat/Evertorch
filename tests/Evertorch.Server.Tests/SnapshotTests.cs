using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class SnapshotTests
{
    [Test]
    public void Snapshot_AcknowledgesEachClientsOwnInputs()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        server.SendMove(first, 40, 1f, 0f);
        server.SendMove(second, 7, 0f, 1f);

        server.Tick();

        Assert.That(server.Transport.SnapshotsSentTo(first).Last().LastProcessedInputSequence, Is.EqualTo(40u));
        Assert.That(server.Transport.SnapshotsSentTo(second).Last().LastProcessedInputSequence, Is.EqualTo(7u));
    }

    [Test]
    public void Snapshot_BeforeAnyInput_AcknowledgesZero()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);

        Assert.That(server.Transport.SnapshotsSentTo(connection).Last().LastProcessedInputSequence, Is.EqualTo(0u));
    }

    [Test]
    public void Snapshot_CarriesTheTickOwnStateAndAcknowledgement()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 9, 1f, 0f);

        uint tick = server.Tick();

        EntitySnapshot snapshot = server.Transport.SnapshotsSentTo(connection).Last();
        PlayerEntity player = server.PlayerOf(connection);
        Assert.That(snapshot.ServerTick, Is.EqualTo(tick));
        Assert.That(snapshot.LastProcessedInputSequence, Is.EqualTo(9u));
        Assert.That(snapshot.Entities, Has.Count.EqualTo(1));
        Assert.That(snapshot.Entities[0].Entity, Is.EqualTo(player.Id));
        Assert.That(snapshot.Entities[0].Position, Is.EqualTo(player.Position));
        Assert.That(snapshot.Entities[0].VelocityX, Is.EqualTo(player.VelocityX));
        Assert.That(snapshot.Entities[0].StateFlags, Is.EqualTo(EntityStateFlags.Moving));
    }

    [Test]
    public void Snapshot_ContainsOnlyEntitiesInInterestAreaWithOwnEntityFirst()
    {
        var server = new TestServer(interestCellSize: 4f, interestNeighborRadius: 1);
        ConnectionId observer = server.EnterWorld(1);
        ConnectionId neighbour = server.EnterWorld(2);
        ConnectionId distant = server.Connect();
        server.SendHello(distant);
        server.SendEnterWorld(distant, 3);
        server.AfterCommandsOnce(() => server.Place(distant, 20f, 0.5f));

        server.Tick();

        EntitySnapshot snapshot = server.Transport.SnapshotsSentTo(observer).Last();
        long[] entities = snapshot.Entities.Select(state => state.Entity.Value).ToArray();
        Assert.That(entities[0], Is.EqualTo(server.PlayerOf(observer).Id.Value));
        Assert.That(entities,
            Is.EquivalentTo(new[] { server.PlayerOf(observer).Id.Value, server.PlayerOf(neighbour).Id.Value }));
    }

    [Test]
    public void Snapshot_IsNotSentToASessionThatHasNotEnteredTheWorld()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);

        server.Tick(3);

        Assert.That(server.Transport.SnapshotsSentTo(connection), Is.Empty);
    }

    [Test]
    public void Snapshot_IsSentOnStateChannelUnreliableSequenced()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);

        InMemoryServerTransport.SentMessage snapshot = server.Transport.SentTo(connection)
            .First(message => message.Opcode == MessageOpcode.EntitySnapshot);

        Assert.That(snapshot.Channel, Is.EqualTo(ProtocolChannel.State));
        Assert.That(snapshot.Delivery, Is.EqualTo(MessageDelivery.UnreliableSequenced));
    }

    [Test]
    public void Snapshot_NeverMentionsAnEntityBeforeItsSpawnWasSent()
    {
        var server = new TestServer();
        ConnectionId observer = server.EnterWorld(1);
        server.EnterWorld(2);
        server.Tick(3);

        var spawned = new HashSet<long> { server.PlayerOf(observer).Id.Value };
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.SentTo(observer))
        {
            if (EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn))
            {
                spawned.Add(spawn!.Entity.Value);
            }
            else if (EntitySnapshot.TryRead(message.Payload, out EntitySnapshot? snapshot))
            {
                Assert.That(snapshot!.Entities.Select(state => state.Entity.Value), Is.SubsetOf(spawned));
            }
        }
    }

    [Test]
    public void Snapshot_WhenInterestAreaExceedsMaximum_SplitsAcrossPacketsWithLocalEntityFirst()
    {
        var server = new TestServer();
        ConnectionId observer = server.EnterWorld(1);
        for (long character = 2; character <= 30; character++)
        {
            server.EnterWorld(character);
        }

        uint tick = server.Tick();

        EntitySnapshot[] packets = server.Transport.SnapshotsSentTo(observer)
            .Where(snapshot => snapshot.ServerTick == tick)
            .ToArray();
        Assert.That(packets.Select(packet => packet.Entities.Count), Is.EqualTo(new[] { 24, 6 }));
        Assert.That(packets[0].Entities[0].Entity, Is.EqualTo(server.PlayerOf(observer).Id));
        Assert.That(packets.SelectMany(packet => packet.Entities).Select(state => state.Entity).Distinct().Count(),
            Is.EqualTo(30));
        Assert.That(packets.Select(packet => packet.LastProcessedInputSequence), Is.All.EqualTo(0u));
    }

    [Test]
    public void Snapshot_WithIntervalOfTwo_IsSentEverySecondTick()
    {
        var server = new TestServer(snapshotIntervalTicks: 2);
        ConnectionId connection = server.EnterWorld(1);
        server.Transport.ClearSent();

        server.Tick(6);

        uint[] ticks = server.Transport.SnapshotsSentTo(connection).Select(snapshot => snapshot.ServerTick).ToArray();
        Assert.That(ticks, Is.EqualTo(new uint[] { 2, 4, 6 }));
    }
}
}
