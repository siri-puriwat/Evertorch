using System;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A lost connection leaves its character in the world for the grace period, and entering it again attaches to
///     that same entity with a fresh baseline and the character's command sequence (Network Protocol §3, §8;
///     Persistence §7).
/// </summary>
[TestFixture]
public sealed class ReconnectTests
{
    private const int GraceMs = 1000;
    private const int GraceTicks = GraceMs * TestServer.TickRate / 1000;

    private static TestServer CreateServer(bool withMonsters = false)
    {
        return new TestServer(withMonsters: withMonsters, reconnectGraceMs: GraceMs);
    }

    private static WorldEntered LastWorldEntered(TestServer server, ConnectionId connection)
    {
        InMemoryServerTransport.SentMessage sent = server.Transport.ControlSentTo(connection)
            .Last(message => message.Opcode == MessageOpcode.WorldEntered);
        WorldEntered.TryRead(sent.Payload, out WorldEntered? entered);
        return entered!;
    }

    private static EntityDespawn[] Despawns(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.EntityDespawn)
            .Select(message =>
            {
                EntityDespawn.TryRead(message.Payload, out EntityDespawn despawn);
                return despawn;
            })
            .ToArray();
    }

    private static EntitySpawn[] Spawns(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.EntitySpawn)
            .Select(message =>
            {
                EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn);
                return spawn!;
            })
            .ToArray();
    }

    [Test]
    public void Disconnect_EndsTheCharactersTargetAndAutoAttack()
    {
        TestServer server = CreateServer(true);
        ConnectionId connection = server.EnterWorld(2);
        EntityId slime = server.MonstersNear(server.PlayerOf(connection).Position).First().Id;
        server.SendAttack(connection, slime, 1);
        server.Tick();
        PlayerEntity entity = server.PlayerOf(connection);

        server.Disconnect(connection);
        server.Tick();

        Assert.That(entity.Target, Is.EqualTo(default(EntityId)));
        Assert.That(entity.Combat.IsAutoAttacking, Is.False);
    }

    [Test]
    public void Disconnect_WhileLoggingOut_RemovesTheCharacterAtOnce()
    {
        TestServer server = CreateServer();
        ConnectionId connection = server.EnterWorld(2);
        server.RunsPersistence = false;
        server.SendLogout(connection, 1);
        server.Tick();

        server.Disconnect(connection);
        server.Tick();

        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Sessions.Characters, Is.Empty);
    }

    [Test]
    public void Disconnect_WithAGracePeriod_LeavesTheCharacterStandingStillInTheWorld()
    {
        TestServer server = CreateServer();
        ConnectionId observer = server.EnterWorld(1);
        ConnectionId leaver = server.EnterWorld(2);
        server.SendMove(leaver, 1, 1f, 0f);
        server.Tick(3);
        PlayerEntity entity = server.PlayerOf(leaver);
        server.Transport.ClearSent();

        server.Disconnect(leaver);
        server.Tick(GraceTicks - 1);

        Assert.That(server.World.Maps.Single().Players, Does.Contain(entity));
        Assert.That(entity.Owner, Is.EqualTo(default(ConnectionId)));
        Assert.That(entity.StateFlags & EntityStateFlags.Moving, Is.EqualTo(EntityStateFlags.None));
        Assert.That(entity.VelocityX, Is.Zero);
        Assert.That(Despawns(server, observer), Is.Empty, "observers still see the character");
        Assert.That(server.Store.Checkpoints, Is.Empty, "the checkpoint waits for the end of the grace period");
    }

    [Test]
    public void GracePeriod_WhenItEnds_CheckpointsAndRemovesTheCharacter()
    {
        TestServer server = CreateServer();
        ConnectionId observer = server.EnterWorld(1);
        ConnectionId leaver = server.EnterWorld(2);
        EntityId entity = server.PlayerOf(leaver).Id;
        server.Transport.ClearSent();

        server.Disconnect(leaver);
        server.Tick(GraceTicks + 2);

        Assert.That(server.World.Maps.Single().Players.Select(player => player.Id), Has.No.Member(entity));
        Assert.That(server.Sessions.Characters.Select(character => character.Character.Value),
            Is.EqualTo(new[] { 1L }));
        Assert.That(server.Store.Checkpoints.Select(checkpoint => checkpoint.CharacterId), Is.EqualTo(new[] { 2L }));
        Assert.That(Despawns(server, observer).Single().Reason, Is.EqualTo(DespawnReason.Removed));
    }

    [Test]
    public void Reconnect_AfterTheGracePeriodEnded_LoadsTheCharacterAgain()
    {
        TestServer server = CreateServer();
        ConnectionId first = server.EnterWorld(2);
        server.Place(first, 3.5f, 4.5f);
        server.Disconnect(first);
        server.Tick(GraceTicks + 2);

        ConnectionId second = server.EnterWorld(2);

        Assert.That(server.Store.Loads[2], Is.EqualTo(2));
        Assert.That(server.PlayerOf(second).Position.X, Is.EqualTo(3.5f));
    }

    [Test]
    public void Reconnect_ContinuesTheCharactersCommandSequence()
    {
        TestServer server = CreateServer(true);
        ConnectionId first = server.EnterWorld(2);
        EntityId slime = server.MonstersNear(server.PlayerOf(first).Position).First().Id;
        server.SendCancel(first, 5);
        server.Tick();
        server.Disconnect(first);
        server.Tick(2);

        ConnectionId second = server.EnterWorld(2);
        server.SendAttack(second, slime, 3);
        server.Tick();
        long refusedAfterStale = server.SessionOf(second).RefusedCommands;
        server.SendAttack(second, slime, 6);
        server.Tick();

        Assert.That(LastWorldEntered(server, second).LastCommandSequence, Is.EqualTo(5u));
        Assert.That(refusedAfterStale, Is.EqualTo(1), "a sequence the character already used is stale");
        Assert.That(server.PlayerOf(second).Target, Is.EqualTo(slime));
        Assert.That(server.SessionOf(second).LastCommandSequence, Is.EqualTo(6u));
    }

    [Test]
    public void Reconnect_GetsAFreshBaselineOfEverythingInView()
    {
        TestServer server = CreateServer(true);
        ConnectionId other = server.EnterWorld(1);
        ConnectionId first = server.EnterWorld(2);
        server.Tick();
        int seenBefore = Spawns(server, first).Length;
        server.Disconnect(first);
        server.Tick(2);

        ConnectionId second = server.EnterWorld(2);
        server.Tick();

        MessageOpcode[] opcodes = server.Transport.ControlOpcodesSentTo(second).ToArray();
        int entered = Array.IndexOf(opcodes, MessageOpcode.WorldEntered);
        EntitySpawn[] spawns = Spawns(server, second);
        Assert.That(entered, Is.GreaterThanOrEqualTo(0));
        Assert.That(opcodes.Skip(entered + 1).First(), Is.EqualTo(MessageOpcode.CharacterSheet));
        Assert.That(opcodes.Skip(entered + 2).First(), Is.EqualTo(MessageOpcode.EntitySpawn));
        Assert.That(spawns.Length, Is.EqualTo(seenBefore));
        Assert.That(spawns.Select(spawn => spawn.Entity), Does.Contain(server.PlayerOf(other).Id));
        Assert.That(spawns.Count(spawn => spawn.Kind == EntityKind.Monster), Is.GreaterThan(0));
    }

    [Test]
    public void Reconnect_ToACharacterThatDiedDuringItsGrace_EntersDeadAndCanRespawn()
    {
        TestServer server = CreateServer();
        ConnectionId first = server.EnterWorld(2);
        PlayerEntity entity = server.PlayerOf(first);
        server.Disconnect(first);
        server.Tick();
        server.Combat.Kill(server.World.Maps.Single(), entity, null, server.CurrentTick);

        ConnectionId second = server.EnterWorld(2);
        WorldEntered entered = LastWorldEntered(server, second);
        server.SendRespawn(second, 1);
        server.Tick();

        Assert.That(entered.CurrentHealth, Is.Zero);
        Assert.That(server.PlayerOf(second).IsDead, Is.False, "the respawn was accepted");
        Assert.That(server.PlayerOf(second).Id, Is.EqualTo(entity.Id));
    }

    [Test]
    public void Reconnect_WhileTheOlderConnectionIsOpen_ClosesItWithSessionReplaced()
    {
        TestServer server = CreateServer();
        ConnectionId older = server.EnterWorld(2);
        EntityId entity = server.PlayerOf(older).Id;

        ConnectionId newer = server.EnterWorld(2);

        Assert.That(server.Transport.Disconnects[older], Is.EqualTo(DisconnectReason.SessionReplaced));
        Assert.That(server.PlayerOf(newer).Id, Is.EqualTo(entity));
        Assert.That(server.Sessions.Characters.Single().GraceEndsTick, Is.Null);
    }

    [Test]
    public void Reconnect_WithinTheGracePeriod_AttachesToTheSameEntityWithoutADespawn()
    {
        TestServer server = CreateServer();
        ConnectionId observer = server.EnterWorld(1);
        ConnectionId first = server.EnterWorld(2);
        EntityId entity = server.PlayerOf(first).Id;
        server.Disconnect(first);
        server.Tick(3);
        server.Transport.ClearSent();

        ConnectionId second = server.EnterWorld(2);
        server.Tick();

        Assert.That(server.PlayerOf(second).Id, Is.EqualTo(entity));
        Assert.That(server.PlayerOf(second).Owner, Is.EqualTo(second));
        Assert.That(server.Store.Loads[2], Is.EqualTo(1), "no second copy is loaded");
        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(2));
        Assert.That(Despawns(server, observer), Is.Empty);
        Assert.That(Spawns(server, observer), Is.Empty, "the observer never lost the entity");
        Assert.That(LastWorldEntered(server, second).LocalEntity, Is.EqualTo(entity));
    }
}
}
