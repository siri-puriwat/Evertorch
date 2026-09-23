using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Milestone 4 verification "disconnect and reconnect keep one character session and give a fresh world and
///     inventory baseline" (Network Protocol §3, §9; Persistence §7).
/// </summary>
[TestFixture]
public sealed class ReconnectBaselineTests
{
    private const int GraceMs = 1000;
    private const int GraceTicks = GraceMs * TestServer.TickRate / 1000;
    private const string SlimeGel = "item.material.slime_gel";

    private static ConnectionId Reenter(TestServer server, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    private static InventorySnapshot[] Snapshots(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.InventorySnapshot)
            .Select(message =>
            {
                InventorySnapshot.TryRead(message.Payload, out InventorySnapshot? part);
                return part!;
            })
            .ToArray();
    }

    private static ItemDropEntity DropNextTo(TestServer server, ConnectionId connection)
    {
        PlayerEntity player = server.PlayerOf(connection);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            3,
            new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    [Test]
    public void Pickup_ThatSettlesWhileDisconnected_IsInTheNextBaseline()
    {
        var server = new TestServer(reconnectGraceMs: GraceMs);
        ConnectionId first = server.EnterWorld(7);
        ItemDropEntity drop = DropNextTo(server, first);
        server.RunsPersistence = false;
        server.SendPickup(first, drop.Id, 1);
        server.Tick();
        server.Disconnect(first);
        server.Tick();
        server.RunsPersistence = true;
        server.Tick(3);

        ConnectionId second = Reenter(server, 7);

        InventorySnapshot snapshot = Snapshots(server, second).Single();
        Assert.That(snapshot.Revision, Is.EqualTo(1u));
        Assert.That(snapshot.Entries.Single().Quantity, Is.EqualTo(3u));
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }

    [Test]
    public void Reconnect_AfterGrace_LoadsTheCharacterExactlyOnceMore()
    {
        var server = new TestServer(reconnectGraceMs: GraceMs);
        ConnectionId first = server.EnterWorld(7);
        EntityId entity = server.PlayerOf(first).Id;
        server.Disconnect(first);
        server.Tick(GraceTicks + 2);

        ConnectionId second = Reenter(server, 7);

        Assert.That(server.Store.Loads[7], Is.EqualTo(2));
        Assert.That(server.PlayerOf(second).Id, Is.Not.EqualTo(entity));
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));
        Assert.That(Snapshots(server, second), Has.Length.EqualTo(1));
    }

    [Test]
    public void Reconnect_WithinGrace_KeepsTheEntityLoadsNothingAndSendsTheWholeBaselineInOrder()
    {
        var server = new TestServer(reconnectGraceMs: GraceMs);
        ConnectionId observer = server.EnterWorld(2);
        ConnectionId first = server.EnterWorld(7);
        ItemDropEntity drop = DropNextTo(server, first);
        server.SendPickup(first, drop.Id, 1);
        server.Tick(3);
        EntityId entity = server.PlayerOf(first).Id;
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = Reenter(server, 7);

        Assert.That(server.PlayerOf(second).Id, Is.EqualTo(entity));
        Assert.That(server.Store.Loads[7], Is.EqualTo(1));
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(2));
        MessageOpcode[] baseline = server.Transport.ControlOpcodesSentTo(second)
            .SkipWhile(opcode => opcode != MessageOpcode.WorldEntered)
            .ToArray();
        Assert.That(baseline.First(), Is.EqualTo(MessageOpcode.WorldEntered));
        Assert.That(baseline.Skip(1).TakeWhile(opcode => opcode == MessageOpcode.EntitySpawn).Count(),
            Is.GreaterThanOrEqualTo(1), "the observer is spawned afresh");
        Assert.That(baseline.Last(), Is.EqualTo(MessageOpcode.InventorySnapshot));
        InventorySnapshot snapshot = Snapshots(server, second).Single();
        Assert.That(snapshot.Revision, Is.EqualTo(1u));
        Assert.That(snapshot.Entries.Single().Quantity, Is.EqualTo(3u));
        EntityId[] despawned = server.Transport.ControlSentTo(observer)
            .Where(message => message.Opcode == MessageOpcode.EntityDespawn)
            .Select(message =>
            {
                EntityDespawn.TryRead(message.Payload, out EntityDespawn despawn);
                return despawn.Entity;
            })
            .ToArray();
        Assert.That(despawned, Has.None.EqualTo(entity), "nobody saw the character leave");
    }

    [Test]
    public void TwoQuickReconnects_LeaveOneCharacterSessionAndOneLoad()
    {
        var server = new TestServer(reconnectGraceMs: GraceMs);
        ConnectionId first = server.EnterWorld(7);
        EntityId entity = server.PlayerOf(first).Id;
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = Reenter(server, 7);
        ConnectionId third = Reenter(server, 7);

        Assert.That(server.Transport.Disconnects[second], Is.EqualTo(DisconnectReason.SessionReplaced));
        Assert.That(server.PlayerOf(third).Id, Is.EqualTo(entity));
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));
        Assert.That(server.Sessions.Characters, Has.Count.EqualTo(1));
        Assert.That(server.Store.Loads[7], Is.EqualTo(1));
        Assert.That(Snapshots(server, third), Has.Length.EqualTo(1));
    }
}
}
