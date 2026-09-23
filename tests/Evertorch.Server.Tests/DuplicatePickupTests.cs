using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Asking twice, or two players asking at once, never makes more than one item (Gameplay Systems §11, §13:
///     "Two players racing to pick up the same drop results in one durable owner").
/// </summary>
[TestFixture]
public sealed class DuplicatePickupTests
{
    private const string SlimeGel = "item.material.slime_gel";

    private static ItemDropEntity DropBetween(TestServer server, params ConnectionId[] pickers)
    {
        PlayerEntity first = server.PlayerOf(pickers[0]);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId(SlimeGel),
            2,
            new WorldPosition(first.Position.X + 0.5f, first.Position.Y, first.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        return drop;
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private static int Changes(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlOpcodesSentTo(connection)
            .Count(opcode => opcode == MessageOpcode.InventoryChanged);
    }

    private static int Held(TestServer server, long character)
    {
        return server.Store.Stored(character).Items.Sum(item => item.Quantity);
    }

    [Test]
    public void SameClient_AskingAgainAfterTheCommit_FindsNoDrop()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = DropBetween(server, picker);
        server.SendPickup(picker, drop.Id, 1);
        server.Tick(3);
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 2);
        server.Tick(3);

        Assert.That(Held(server, 1), Is.EqualTo(2));
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(Changes(server, picker), Is.Zero);
        Assert.That(Rejections(server, picker).Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void SameClient_AskingTwiceInOneTick_GetsOneItemAndABusyRefusal()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = DropBetween(server, picker);
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 1);
        server.SendPickup(picker, drop.Id, 2);
        server.Tick(3);

        Assert.That(Held(server, 1), Is.EqualTo(2));
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(server.Store.PickupCommits, Has.Count.EqualTo(1));
        Assert.That(Changes(server, picker), Is.EqualTo(1));
        CommandRejected refusal = Rejections(server, picker).Single();
        Assert.That(refusal.CommandSequence, Is.EqualTo(2u));
        Assert.That(refusal.Reason, Is.EqualTo(CommandRejectionReason.Busy));
    }

    [Test]
    public void StaleSequence_ForAPickup_IsIgnoredWithoutReply()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity first = DropBetween(server, picker);
        server.SendPickup(picker, first.Id, 5);
        server.Tick(3);
        ItemDropEntity second = DropBetween(server, picker);
        server.Transport.ClearSent();

        server.SendPickup(picker, second.Id, 5);
        server.Tick(3);

        Assert.That(server.World.Maps.Single().Contains(second.Id), Is.True);
        Assert.That(server.Transport.ControlSentTo(picker), Is.Empty);
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
    }

    [Test]
    public void TwoClients_AskingInTheSameTick_MakeOneOwner()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        ItemDropEntity drop = DropBetween(server, first, second);
        server.Transport.ClearSent();

        server.SendPickup(second, drop.Id, 1);
        server.SendPickup(first, drop.Id, 1);
        server.Tick(3);

        Assert.That(Held(server, 2), Is.EqualTo(2), "the request handled first reserves the drop");
        Assert.That(Held(server, 1), Is.Zero);
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(Changes(server, second), Is.EqualTo(1));
        Assert.That(Changes(server, first), Is.Zero);
        Assert.That(Rejections(server, first).Single().Reason, Is.EqualTo(CommandRejectionReason.Busy));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(first).Count(opcode => opcode == MessageOpcode.ItemPickedUp),
            Is.EqualTo(1),
            "the loser still sees who took it");
    }

    [Test]
    public void TwoClients_TheLoserRetryingAfterTheCommit_FindsNoDrop()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        ItemDropEntity drop = DropBetween(server, first, second);
        server.SendPickup(first, drop.Id, 1);
        server.SendPickup(second, drop.Id, 1);
        server.Tick(3);
        server.Transport.ClearSent();

        server.SendPickup(second, drop.Id, 2);
        server.Tick(3);

        Assert.That(Held(server, 1), Is.EqualTo(2));
        Assert.That(Held(server, 2), Is.Zero);
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(Rejections(server, second).Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }
}
}
