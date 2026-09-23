using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class PlayerDeathTests
{
    private static void Kill(TestServer server, ConnectionId connection)
    {
        MapInstance map = server.World.Maps.Single();
        server.Combat.Kill(map, server.PlayerOf(connection), null, server.CurrentTick);
    }

    private static EntityRevived ReadRevived(InMemoryServerTransport.SentMessage message)
    {
        Assert.That(EntityRevived.TryRead(message.Payload, out EntityRevived revived), Is.True);
        return revived;
    }

    private static CharacterHealth ReadHealth(InMemoryServerTransport.SentMessage message)
    {
        Assert.That(CharacterHealth.TryRead(message.Payload, out CharacterHealth health), Is.True);
        return health;
    }

    [Test]
    public void Dead_Player_AppliesNoMovementAndRefusesEveryCommandButRespawn()
    {
        var rig = new CombatRig();
        Kill(rig.Server, rig.Player);
        rig.Server.Tick();
        WorldPosition corpse = rig.Entity.Position;

        rig.Move(1f, 0f);
        rig.Attack(rig.Slime.Id);
        rig.Cancel();
        rig.Server.SendTarget(rig.Player, rig.Slime.Id);
        rig.Server.Tick(5);

        Assert.That(rig.Entity.IsDead, Is.True);
        Assert.That(rig.Entity.Position, Is.EqualTo(corpse));
        Assert.That(rig.Entity.StateFlags, Is.EqualTo(EntityStateFlags.Dead));
        Assert.That(rig.Entity.Target, Is.EqualTo(default(EntityId)));
        Assert.That(rig.Entity.Combat.IsAutoAttacking, Is.False);
        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.EqualTo(3));
    }

    [Test]
    public void Respawn_ConsumesTheMovesSentWhileDead()
    {
        var rig = new CombatRig();
        Kill(rig.Server, rig.Player);
        rig.Server.Tick();

        rig.Move(1f, 0f);
        rig.Move(1f, 0f);
        rig.Server.SendRespawn(rig.Player, 1);
        rig.Server.Tick(3);

        Assert.That(rig.Entity.Position, Is.EqualTo(rig.Map.Definition.SpawnPosition));
        Assert.That(rig.Server.SessionOf(rig.Player).Input!.LastProcessedSequence, Is.EqualTo(2u));
    }

    [Test]
    public void Respawn_FarFromAnObserver_IsToldToItBeforeTheDespawn()
    {
        var server = new TestServer(interestCellSize: 4f, interestNeighborRadius: 0);
        ConnectionId owner = server.EnterWorld(1);
        ConnectionId observer = server.EnterWorld(2);
        server.Place(owner, 8f, 8f);
        server.Place(observer, 8f, 8f);
        server.Tick();
        EntityId ownerId = server.PlayerOf(owner).Id;
        Kill(server, owner);
        server.Tick();
        server.Transport.ClearSent();

        server.SendRespawn(owner, 1);
        server.Tick();

        var sent = server.Transport.ControlSentTo(observer).ToList();
        Assert.That(
            sent.Select(message => message.Opcode),
            Is.EqualTo(new[] { MessageOpcode.EntityRevived, MessageOpcode.EntityDespawn }));
        Assert.That(ReadRevived(sent[0]).Entity, Is.EqualTo(ownerId));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(owner),
            Is.EqualTo(
                new[] { MessageOpcode.EntityRevived, MessageOpcode.CharacterHealth, MessageOpcode.EntityDespawn }));
    }

    [Test]
    public void Respawn_Twice_RefusesTheSecond()
    {
        var rig = new CombatRig();
        Kill(rig.Server, rig.Player);
        rig.Server.Tick();

        rig.Server.SendRespawn(rig.Player, 1);
        rig.Server.Tick();
        rig.Move(1f, 0f);
        rig.Server.Tick();
        WorldPosition moved = rig.Entity.Position;
        rig.Server.SendRespawn(rig.Player, 2);
        rig.Server.Tick();

        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.EqualTo(1));
        Assert.That(rig.Entity.Position, Is.Not.EqualTo(rig.Map.Definition.SpawnPosition));
        Assert.That(moved, Is.Not.EqualTo(rig.Map.Definition.SpawnPosition), "the living player walked");
    }

    [Test]
    public void Respawn_WhileDead_RevivesAtTheSpawnPointWithFullHealthThenTellsTheOwnerItsHealth()
    {
        var rig = new CombatRig();
        Kill(rig.Server, rig.Player);
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();

        rig.Server.SendRespawn(rig.Player, 1);
        uint tick = rig.Server.Tick();

        MapDefinition map = rig.Map.Definition;
        Assert.That(rig.Entity.IsDead, Is.False);
        Assert.That(rig.Entity.StateFlags, Is.EqualTo(EntityStateFlags.None));
        Assert.That(rig.Entity.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(rig.Entity.Facing, Is.EqualTo(map.SpawnFacing));
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(71));
        Assert.That(rig.Server.SessionOf(rig.Player).RefusedCommands, Is.Zero);

        var sent = rig.Server.Transport.ControlSentTo(rig.Player).ToList();
        Assert.That(
            sent.Select(message => message.Opcode).Take(2),
            Is.EqualTo(new[] { MessageOpcode.EntityRevived, MessageOpcode.CharacterHealth }));
        EntityRevived revived = ReadRevived(sent[0]);
        Assert.That(revived.Entity, Is.EqualTo(rig.Entity.Id));
        Assert.That(revived.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(revived.Facing, Is.EqualTo(map.SpawnFacing));
        Assert.That(revived.ServerTick, Is.EqualTo(tick));
        CharacterHealth health = ReadHealth(sent[1]);
        Assert.That(health.Current, Is.EqualTo(71u));
        Assert.That(health.Maximum, Is.EqualTo(71u));
    }
}
}
