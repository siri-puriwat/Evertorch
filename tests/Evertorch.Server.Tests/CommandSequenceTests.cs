using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class CommandSequenceTests
{
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
    public void Attack_OnAVisibleMonster_EstablishesTheTarget()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();

        server.SendAttack(player, slime, 1);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(slime));
        Assert.That(server.Transport.ControlOpcodesSentTo(player), Is.EqualTo(new[] { MessageOpcode.TargetChanged }));
        Assert.That(server.SessionOf(player).LastCommandSequence, Is.EqualTo(1u));
        Assert.That(server.SessionOf(player).RefusedCommands, Is.Zero);
    }

    [Test]
    public void Attack_OnAnUntargetableEntity_IsRefusedButUsesItsSequence()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();

        server.SendAttack(player, server.PlayerOf(player).Id, 1);
        server.SendAttack(player, default, 2);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(2));
        Assert.That(server.SessionOf(player).LastCommandSequence, Is.EqualTo(2u));
    }

    [Test]
    public void Command_AfterTheSequenceWrapsAround_IsStillNewer()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.SessionOf(player).LastCommandSequence = uint.MaxValue;

        server.SendAttack(player, slime, 1);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(slime));
        Assert.That(server.SessionOf(player).LastCommandSequence, Is.EqualTo(1u));
    }

    [Test]
    public void Command_BeforeEnteringTheWorld_IsIgnored()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.Tick();

        server.SendCancel(connection, 1);
        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
    }

    [Test]
    public void Command_WithADuplicateOrOlderSequence_IsRefusedAndChangesNothing()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.SendCancel(player, 5);
        server.Tick();

        server.SendAttack(player, slime, 5);
        server.SendAttack(player, slime, 4);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(2));
        Assert.That(server.SessionOf(player).LastCommandSequence, Is.EqualTo(5u));
    }

    [Test]
    public void Command_WithSequenceZero_IsNeverNewerThanTheStart()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();

        server.SendCancel(player, 0);
        server.Tick();

        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(1));
    }

    [Test]
    public void Respawn_WhileAlive_IsRefused()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();
        WorldPosition before = server.PlayerOf(player).Position;

        server.SendRespawn(player, 1);
        server.Tick();

        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(1));
        Assert.That(server.PlayerOf(player).Position, Is.EqualTo(before));
        Assert.That(server.Transport.ControlSentTo(player), Is.Empty);
    }
}
}
