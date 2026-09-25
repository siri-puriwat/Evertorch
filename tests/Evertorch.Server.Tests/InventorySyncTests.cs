using System.Collections.Generic;
using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A client gets its whole inventory after the baseline's spawns on entering and attaching, and again whenever it
///     asks for a resynchronization (Network Protocol §9, §12).
/// </summary>
[TestFixture]
public sealed class InventorySyncTests
{
    private const string SlimeGel = "item.material.slime_gel";

    private static ConnectionId EnterWithItems(TestServer server, long character, int rows, uint revision)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.Store.GiveItems(character, SlimeGel, rows, 5, revision);
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

    [Test]
    public void Attach_SendsTheInventoryAgain()
    {
        var server = new TestServer(reconnectGraceMs: 1000);
        ConnectionId first = EnterWithItems(server, 7, 3, 9);
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = server.Connect();
        server.SignInWithCharacter(second, 7);
        server.SendEnterWorld(second, 7);
        server.TickUntil(() => server.SessionOf(second).State == SessionState.InWorld);

        InventorySnapshot part = Snapshots(server, second).Single();
        Assert.That(part.Revision, Is.EqualTo(9u));
        Assert.That(part.Entries, Has.Count.EqualTo(3));
        Assert.That(server.Store.Loads[7], Is.EqualTo(1), "the attach reuses the loaded inventory");
    }

    [Test]
    public void Enter_SendsTheWholeInventoryInPartsAfterTheBaselineSpawns()
    {
        var server = new TestServer();
        server.EnterWorld(1);

        ConnectionId connection = EnterWithItems(server, 2, 100, 42);

        IReadOnlyList<MessageOpcode> opcodes = server.Transport.ControlOpcodesSentTo(connection);
        int entered = opcodes.ToList().IndexOf(MessageOpcode.WorldEntered);
        int lastSpawn = opcodes.ToList().LastIndexOf(MessageOpcode.EntitySpawn);
        int firstPart = opcodes.ToList().IndexOf(MessageOpcode.InventorySnapshot);
        Assert.That(lastSpawn, Is.GreaterThan(entered));
        Assert.That(firstPart, Is.GreaterThan(lastSpawn));
        Assert.That(
            opcodes.Skip(firstPart),
            Is.EqualTo(Enumerable.Repeat(MessageOpcode.InventorySnapshot, 9).Append(MessageOpcode.SkillList)),
            "the parts, then the skill list that ends the baseline");

        InventorySnapshot[] parts = Snapshots(server, connection);
        Assert.That(parts, Has.Length.EqualTo(9));
        Assert.That(parts.Select(part => part.Revision), Is.All.EqualTo(42u));
        Assert.That(parts.Select(part => (int)part.Part), Is.EqualTo(Enumerable.Range(0, 9)));
        Assert.That(
            parts.SelectMany(part => part.Entries).Select(entry => entry.InventoryItem),
            Is.EqualTo(server.Store.Stored(2).Items.Select(item => item.Id)));
        Assert.That(parts.SelectMany(part => part.Entries).Select(entry => entry.Quantity), Is.All.EqualTo(5u));
    }

    [Test]
    public void Enter_WithAnEmptyInventory_SendsOneEmptyPart()
    {
        var server = new TestServer();

        ConnectionId connection = server.EnterWorld(7);

        InventorySnapshot part = Snapshots(server, connection).Single();
        Assert.That(part.Revision, Is.EqualTo(0u));
        Assert.That(part.IsLast, Is.True);
        Assert.That(part.Entries, Is.Empty);
    }

    [Test]
    public void RealClient_AfterEntering_HoldsTheInventoryOverALinkWithLatency()
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, 1, 17);
        client.Link.LatencyMilliseconds = 80;
        rig.ConnectAll();

        int took = rig.AdvanceUntil(() => client.Connection.World?.Inventory.IsCurrent == true, 3000);

        Assert.That(took, Is.GreaterThan(0));
        Assert.That(client.World.Inventory.Revision, Is.EqualTo(0u));
        Assert.That(client.World.Inventory.Rows, Is.Empty);
        Assert.That(client.Connection.MalformedMessages + client.Connection.UnexpectedMessages, Is.Zero);
    }

    [Test]
    public void ResyncRequest_InTheWorld_IsAnsweredWithTheWholeInventory()
    {
        var server = new TestServer();
        ConnectionId connection = EnterWithItems(server, 7, 13, 4);
        server.Transport.ClearSent();

        server.SendInventoryResync(connection);
        server.Tick();

        InventorySnapshot[] parts = Snapshots(server, connection);
        Assert.That(server.Transport.ControlOpcodesSentTo(connection), Is.All.EqualTo(MessageOpcode.InventorySnapshot));
        Assert.That(parts.Select(part => part.PartCount), Is.All.EqualTo((byte)2));
        Assert.That(parts.SelectMany(part => part.Entries).Count(), Is.EqualTo(13));
        Assert.That(parts.Select(part => part.Revision), Is.All.EqualTo(4u));
    }

    [Test]
    public void ResyncRequest_OutsideTheWorld_IsIgnored()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 7);
        server.Transport.ClearSent();
        long ignoredBefore = server.SessionManager.IgnoredEvents;

        server.SendInventoryResync(connection);
        server.Tick();

        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(ignoredBefore + 1));
    }

    [Test]
    public void ResyncRequest_SeveralInOneTick_AreAnsweredOnce()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Transport.ClearSent();

        server.SendInventoryResync(connection);
        server.SendInventoryResync(connection);
        server.SendInventoryResync(connection);
        server.Tick();

        Assert.That(Snapshots(server, connection), Has.Length.EqualTo(1));
    }
}
}
