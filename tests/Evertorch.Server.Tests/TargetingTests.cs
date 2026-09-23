using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class TargetingTests
{
    private static TargetChanged[] TargetChanges(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.TargetChanged)
            .Select(message =>
            {
                TargetChanged.TryRead(message.Payload, out TargetChanged changed);
                return changed;
            })
            .ToArray();
    }

    private static (TestServer Server, ConnectionId Player, EntityId Slime) EnterNearSlimes()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick();
        EntityId slime = server.MonstersNear(server.PlayerOf(player).Position).First().Id;
        server.Transport.ClearSent();
        return (server, player, slime);
    }

    [Test]
    public void TargetLeavingView_ClearsTheTargetAfterItsDespawn()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.SendTarget(player, slime);
        server.Tick();
        server.Transport.ClearSent();

        server.Place(player, -20f, -20f);
        server.Tick();

        IReadOnlyList<MessageOpcode> opcodes = server.Transport.ControlOpcodesSentTo(player);
        int despawn = server.Transport.ControlSentTo(player)
            .ToList()
            .FindIndex(message =>
                EntityDespawn.TryRead(message.Payload, out EntityDespawn gone) && gone.Entity == slime);
        Assert.That(despawn, Is.GreaterThanOrEqualTo(0));
        Assert.That(opcodes.Skip(despawn + 1), Does.Contain(MessageOpcode.TargetChanged));
        Assert.That(TargetChanges(server, player).Single().Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Target_BeforeEnteringTheWorld_IsIgnored()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId connection = server.Connect();
        server.Tick();

        server.SendTarget(connection, new EntityId(1));
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.Inbound.Malformed, Is.Zero);
    }

    [Test]
    public void Target_ForAMonsterOutOfView_IsRefusedLikeAMissingOne()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.Place(player, -20f, -20f);
        server.Tick();
        server.Transport.ClearSent();

        server.SendTarget(player, slime);
        server.SendTarget(player, new EntityId(9999));
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.Transport.ControlSentTo(player), Is.Empty);
        Assert.That(server.Sessions.Sessions.Single().RefusedCommands, Is.EqualTo(2));
    }

    [Test]
    public void Target_ForAMonsterTheClientKnows_IsAcceptedAndConfirmedToTheOwner()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();

        server.SendTarget(player, slime);
        server.Tick();

        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(entity.Target, Is.EqualTo(slime));
        TargetChanged changed = TargetChanges(server, player).Single();
        Assert.That(changed.Actor, Is.EqualTo(entity.Id));
        Assert.That(changed.Target, Is.EqualTo(slime));
        Assert.That(server.SessionManager.IgnoredEvents, Is.Zero);
    }

    [Test]
    public void Target_ForAnotherPlayer_IsRefused()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        server.Tick();
        server.Transport.ClearSent();

        server.SendTarget(first, server.PlayerOf(second).Id);
        server.Tick();

        Assert.That(server.PlayerOf(first).Target, Is.EqualTo(default(EntityId)));
        Assert.That(TargetChanges(server, first), Is.Empty);
        server.Tick(TestServer.TickRate);
        PlayerSummary summary = server.Status.Current.Players.Single(p => p.Connection == first);
        Assert.That(summary.RefusedCommands, Is.EqualTo(1));
    }

    [Test]
    public void Target_ForEntityZero_ClearsTheTarget()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.SendTarget(player, slime);
        server.Tick();
        server.Transport.ClearSent();

        server.SendTarget(player, default);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
        Assert.That(TargetChanges(server, player).Single().Target, Is.EqualTo(default(EntityId)));
    }
}
}
