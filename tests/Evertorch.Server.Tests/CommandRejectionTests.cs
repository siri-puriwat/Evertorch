using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Every refused command that carries a command sequence is answered with that sequence and a reason; a stale
///     sequence and <c>TargetEntity</c> are not answered (Network Protocol §11).
/// </summary>
[TestFixture]
public sealed class CommandRejectionTests
{
    private static (TestServer Server, ConnectionId Player, EntityId Slime) EnterNearSlimes()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId player = server.EnterWorld(1);
        server.Tick();
        EntityId slime = server.MonstersNear(server.PlayerOf(player).Position).First().Id;
        server.Transport.ClearSent();
        return (server, player, slime);
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    private static void Kill(TestServer server, ConnectionId player)
    {
        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(player), null, server.CurrentTick);
        server.Transport.ClearSent();
    }

    [TestCase(InboundEventKind.Attack)]
    [TestCase(InboundEventKind.Cancel)]
    [TestCase(InboundEventKind.Logout)]
    [TestCase(InboundEventKind.Equip)]
    [TestCase(InboundEventKind.Unequip)]
    public void Command_WhileDead_IsRejectedAsNotAllowedNow(InboundEventKind kind)
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        Kill(server, player);

        switch (kind)
        {
            case InboundEventKind.Attack:
                server.SendAttack(player, slime, 7);
                break;
            case InboundEventKind.Cancel:
                server.SendCancel(player, 7);
                break;
            case InboundEventKind.Equip:
                // A row it does not have would be reason 1; death is checked first.
                server.SendEquip(player, 999999, 7);
                break;
            case InboundEventKind.Unequip:
                server.SendUnequip(player, EquipmentSlot.Weapon, 7);
                break;
            default:
                server.SendLogout(player, 7);
                break;
        }

        server.Tick();

        CommandRejected rejected = Rejections(server, player).Single();
        Assert.That(rejected.CommandSequence, Is.EqualTo(7u));
        Assert.That(rejected.Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
    }

    [Test]
    public void Attack_OnADeadMonster_IsRejectedExactlyLikeAMissingOne()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        MapInstance map = server.World.Maps.Single();
        map.TryGetMonster(slime, out MonsterEntity? monster);
        server.Combat.Kill(map, monster!, null, server.CurrentTick);
        server.Transport.ClearSent();

        server.SendAttack(player, slime, 1);
        server.Tick();

        Assert.That(Rejections(server, player).Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Attack_OnAVisibleMonster_IsNotRejected()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();

        server.SendAttack(player, slime, 1);
        server.Tick();

        Assert.That(Rejections(server, player), Is.Empty);
    }

    [Test]
    public void Attack_OnAnUntargetableEntity_IsRejectedAsAnInvalidTargetWithItsSequence()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();

        server.SendAttack(player, server.PlayerOf(player).Id, 4);
        server.SendAttack(player, default, 5);
        server.SendAttack(player, new EntityId(987654), 6);
        server.Tick();

        CommandRejected[] rejected = Rejections(server, player);
        Assert.That(rejected.Select(message => message.CommandSequence), Is.EqualTo(new[] { 4u, 5u, 6u }));
        Assert.That(rejected.Select(message => message.Reason), Is.All.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Command_WhileLoggingOut_IsRejectedAsNotAllowedNow()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.RunsPersistence = false;
        server.SendLogout(player, 1);
        server.Tick();

        server.SendLogout(player, 2);
        server.SendAttack(player, slime, 3);
        server.Tick();

        Assert.That(
            Rejections(server, player).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[]
                { (2u, CommandRejectionReason.NotAllowedNow), (3u, CommandRejectionReason.NotAllowedNow) }));
    }

    [Test]
    public void Command_WithAStaleSequence_IsCountedButNotAnswered()
    {
        (TestServer server, ConnectionId player, EntityId slime) = EnterNearSlimes();
        server.SendCancel(player, 5);
        server.Tick();
        server.Transport.ClearSent();

        server.SendAttack(player, slime, 5);
        server.SendRespawn(player, 2);
        server.Tick();

        Assert.That(Rejections(server, player), Is.Empty);
        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(2));
    }

    [Test]
    public void Logout_WhileTheDatabaseIsUnavailable_IsRejectedAsServiceUnavailable()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();
        server.Store.IsUnavailable = true;
        server.Persistence.Probe();

        server.SendLogout(player, 9);
        server.Tick();

        CommandRejected rejected = Rejections(server, player).Single();
        Assert.That(rejected.CommandSequence, Is.EqualTo(9u));
        Assert.That(rejected.Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
    }

    [Test]
    public void Respawn_WhileAlive_IsRejectedAsNotAllowedNow()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();

        server.SendRespawn(player, 3);
        server.Tick();

        CommandRejected rejected = Rejections(server, player).Single();
        Assert.That(rejected.CommandSequence, Is.EqualTo(3u));
        Assert.That(rejected.Reason, Is.EqualTo(CommandRejectionReason.NotAllowedNow));
    }

    [Test]
    public void TargetEntity_WhenRefused_IsNotAnswered()
    {
        (TestServer server, ConnectionId player, EntityId _) = EnterNearSlimes();

        server.SendTarget(player, server.PlayerOf(player).Id);
        server.Tick();

        Assert.That(server.Transport.ControlSentTo(player), Is.Empty);
        Assert.That(server.SessionOf(player).RefusedCommands, Is.EqualTo(1));
    }
}
}
