using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Characters are loaded from storage, checkpointed on an interval, on disconnect, and on logout, and a character
///     checkpointed dead comes back alive at the spawn point (Persistence §6, §7; Gameplay Systems §10.1).
/// </summary>
[TestFixture]
public sealed class CharacterLifetimeTests
{
    private static readonly WorldPosition Stored = new(3.5f, 0f, 4.5f);

    private static WorldEntered LastWorldEntered(TestServer server, ConnectionId connection)
    {
        InMemoryServerTransport.SentMessage sent = server.Transport.ControlSentTo(connection)
            .Last(message => message.Opcode == MessageOpcode.WorldEntered);
        WorldEntered.TryRead(sent.Payload, out WorldEntered? entered);
        return entered!;
    }

    private static ConnectionId EnterAgain(TestServer server, long character)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State != SessionState.EnteringWorld);
        return connection;
    }

    [TestCase("job.forgotten", null, null)]
    [TestCase(null, "map.forgotten", null)]
    [TestCase(null, null, "item.material.forgotten")]
    public void Enter_WithADefinitionTheContentLacks_IsRefusedAndTheDataIsKept(string? job, string? map, string? item)
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, job, map, item: item);
        StoredCharacter before = server.Store.Stored(7);
        long ignoredBefore = server.SessionManager.IgnoredEvents;

        ConnectionId connection = EnterAgain(server, 7);

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(ignoredBefore + 1));
        Assert.That(
            server.Log.Entries.Select(entry => entry.EventId.Name),
            Has.Some.EqualTo("CharacterContentMismatch"));
        StoredCharacter after = server.Store.Stored(7);
        Assert.That(after.JobDefinitionId, Is.EqualTo(before.JobDefinitionId));
        Assert.That(after.MapDefinitionId, Is.EqualTo(before.MapDefinitionId));
        Assert.That(after.Items.Select(stored => stored.ItemDefinitionId),
            Is.EqualTo(before.Items.Select(stored => stored.ItemDefinitionId)));
    }

    [Test]
    public void CheckpointAll_OnShutdown_QueuesOneForEveryCharacterInTheWorld()
    {
        var server = new TestServer();
        server.EnterWorld(7);
        server.EnterWorld(8);

        server.Lifetime.CheckpointAll();
        server.Persistence.RunUntilIdle();

        Assert.That(server.Store.Checkpoints.Select(checkpoint => checkpoint.CharacterId),
            Is.EquivalentTo(new[] { 7, 8 }));
    }

    [Test]
    public void Checkpoint_IsWrittenOncePerIntervalFromEntry()
    {
        var server = new TestServer(persistence: new PersistenceOptions { CheckpointIntervalMs = 1000 });
        ConnectionId connection = server.EnterWorld(7);
        uint entered = server.CurrentTick;

        server.Tick(TestServer.TickRate - 1);
        int beforeTheInterval = server.Store.Checkpoints.Count;
        server.Tick(2);
        int afterOne = server.Store.Checkpoints.Count;
        server.Tick(TestServer.TickRate);

        Assert.That(beforeTheInterval, Is.Zero, $"entered on tick {entered}");
        Assert.That(afterOne, Is.EqualTo(1));
        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(2));
        Assert.That(server.Store.Checkpoints.Last().Position, Is.EqualTo(server.PlayerOf(connection).Position));
    }

    [Test]
    public void Disconnect_WhileDead_CheckpointsZeroHealth()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(connection), null, server.CurrentTick);

        server.Disconnect(connection);
        server.Tick(2);

        Assert.That(server.Store.Checkpoints.Single().Health, Is.Zero);
    }

    [Test]
    public void Disconnect_WritesTheCheckpointAndRemovesTheCharacter()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Place(connection, Stored.X, Stored.Z);
        server.PlayerOf(connection).CurrentHealth = 44;

        server.Disconnect(connection);
        server.Tick(2);

        CharacterCheckpoint checkpoint = server.Store.Checkpoints.Single();
        Assert.That(checkpoint.CharacterId, Is.EqualTo(7));
        Assert.That(checkpoint.Position.X, Is.EqualTo(Stored.X));
        Assert.That(checkpoint.Health, Is.EqualTo(44));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Sessions.Characters, Is.Empty);
    }

    [Test]
    public void Enter_BeforeTheLoadAnswers_SendsNothingYet()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 7);
        server.Transport.ClearSent();
        server.SendEnterWorld(connection, 7);

        server.Tick();

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.EnteringWorld));
        Assert.That(server.Transport.ControlSentTo(connection), Is.Empty);
    }

    [Test]
    public void Enter_CheckpointedDead_LoadsAliveAtTheSpawnPointWithFullHealth()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, position: Stored, health: 0);

        ConnectionId connection = EnterAgain(server, 7);

        PlayerEntity player = server.PlayerOf(connection);
        MapDefinition map = server.World.Maps.Single().Definition;
        Assert.That(player.IsDead, Is.False);
        Assert.That(player.Position, Is.EqualTo(map.SpawnPosition));
        Assert.That(player.CurrentHealth, Is.EqualTo(player.MaxHealth));
    }

    [Test]
    public void Enter_LoadsTheStoredPositionAndHealth()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, position: Stored, health: 30);

        ConnectionId connection = EnterAgain(server, 7);

        WorldEntered entered = LastWorldEntered(server, connection);
        Assert.That(entered.Position, Is.EqualTo(Stored));
        Assert.That(entered.CurrentHealth, Is.EqualTo(30u));
        Assert.That(server.PlayerOf(connection).Position, Is.EqualTo(Stored));
        Assert.That(server.PlayerOf(connection).CurrentHealth, Is.EqualTo(30));
    }

    [Test]
    public void Enter_WhereTheStoredSpotIsNoLongerStandable_LoadsAtTheSpawnPoint()
    {
        var server = new TestServer();
        server.Disconnect(server.EnterWorld(7));
        server.Tick(2);
        server.Store.Edit(7, position: new WorldPosition(-999f, 0f, -999f), health: 30);

        ConnectionId connection = EnterAgain(server, 7);

        Assert.That(
            server.PlayerOf(connection).Position,
            Is.EqualTo(server.World.Maps.Single().Definition.SpawnPosition));
        Assert.That(server.PlayerOf(connection).CurrentHealth, Is.EqualTo(30));
    }

    [Test]
    public void Enter_WhileTheDatabaseIsUnreachable_ClosesAsNotReady()
    {
        var server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, 7);
        server.Store.IsUnavailable = true;

        server.SendEnterWorld(connection, 7);
        server.TickUntil(() => server.Transport.Disconnects.ContainsKey(connection));

        Assert.That(server.Transport.Disconnects[connection], Is.EqualTo(DisconnectReason.ServerNotReady));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
    }

    [Test]
    public void LoggingOut_TakesNoNewCommandsOrMovement()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId connection = server.EnterWorld(7);
        server.RunsPersistence = false;
        EntityId slime = server.MonstersNear(server.PlayerOf(connection).Position).First().Id;
        server.SendLogout(connection, 1);
        server.Tick();
        WorldPosition before = server.PlayerOf(connection).Position;

        server.SendAttack(connection, slime, 2);
        server.SendMove(connection, 1, 1f, 0f);
        server.Tick(3);

        Assert.That(server.PlayerOf(connection).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.PlayerOf(connection).Position, Is.EqualTo(before));
        Assert.That(server.SessionOf(connection).RefusedCommands, Is.EqualTo(1));
    }

    [Test]
    public void Logout_AfterAMoveQueuedInTheSameTick_LeavesTheCharacterWhereItsCheckpointWasTaken()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.RunsPersistence = false;
        WorldPosition before = server.PlayerOf(connection).Position;

        server.SendMove(connection, 1, 1f, 0f);
        server.SendLogout(connection, 1);
        server.Tick(3);

        Assert.That(server.PlayerOf(connection).Position, Is.EqualTo(before));
        Assert.That(
            server.SessionOf(connection).Input!.LastProcessedSequence,
            Is.EqualTo(1u),
            "the move is acknowledged, not applied");
    }

    [Test]
    public void Logout_QueuedBehindACheckpointThatMeetsAnOutage_IsCancelledAndTheCharacterPlaysOn()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(7);
        ConnectionId second = server.EnterWorld(8);
        server.SendLogout(first, 1);
        server.SendLogout(second, 1);
        server.Tick();
        server.Store.IsUnavailable = true;

        // The first logout's checkpoint meets the outage; the second one's is never tried while it lasts.
        server.Tick(2);
        WorldPosition before = server.PlayerOf(second).Position;
        server.SendMove(second, 1, 1f, 0f);
        server.Tick(2);

        Assert.That(server.SessionOf(second).Character!.IsLoggingOut, Is.False);
        Assert.That(server.PlayerOf(second).Position, Is.Not.EqualTo(before), "the player plays on");
        InMemoryServerTransport.SentMessage rejected = server.Transport.ControlSentTo(second)
            .Single(message => message.Opcode == MessageOpcode.CommandRejected);
        CommandRejected.TryRead(rejected.Payload, out CommandRejected refusal);
        Assert.That(refusal.CommandSequence, Is.EqualTo(1u));
        Assert.That(refusal.Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(server.Transport.ControlOpcodesSentTo(second), Has.None.EqualTo(MessageOpcode.LogoutComplete));
    }

    [Test]
    public void Logout_ThenEnterAgain_ContinuesFromTheCheckpoint()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Place(connection, Stored.X, Stored.Z);
        server.SendLogout(connection, 1);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.Authenticated);

        server.SendEnterWorld(connection, 7);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);

        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(Stored.X));
        Assert.That(server.Store.Loads[7], Is.EqualTo(2));
    }

    [Test]
    public void Logout_WhileDead_IsRefusedAndTheCharacterStays()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(connection), null, server.CurrentTick);

        server.SendLogout(connection, 1);
        server.Tick(3);

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.InWorld));
        Assert.That(server.SessionOf(connection).RefusedCommands, Is.EqualTo(1));
        Assert.That(server.Store.Checkpoints, Is.Empty);
    }

    [Test]
    public void Logout_WhileTheDatabaseIsKnownUnreachable_IsRefusedAndTheCharacterStays()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Store.IsUnavailable = true;
        server.Persistence.Probe();

        server.SendLogout(connection, 1);
        server.Tick();

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.InWorld));
        Assert.That(server.SessionOf(connection).Character!.IsLoggingOut, Is.False);
        Assert.That(server.SessionOf(connection).RefusedCommands, Is.EqualTo(1));
    }

    [Test]
    public void Logout_WhoseCheckpointCannotBeWritten_IsCancelledAndTheCharacterPlaysOn()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.SendLogout(connection, 1);
        server.Tick();
        server.Store.IsUnavailable = true;

        server.Tick(2);
        server.Store.IsUnavailable = false;
        server.SendCancel(connection, 2);
        server.Tick(2);

        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.InWorld));
        Assert.That(server.SessionOf(connection).Character!.IsLoggingOut, Is.False);
        Assert.That(server.Transport.ControlOpcodesSentTo(connection), Has.None.EqualTo(MessageOpcode.LogoutComplete));
        Assert.That(
            server.SessionOf(connection).RefusedCommands,
            Is.EqualTo(1),
            "only the logout was refused; the cancel after it worked");
        InMemoryServerTransport.SentMessage rejected = server.Transport.ControlSentTo(connection)
            .Single(message => message.Opcode == MessageOpcode.CommandRejected);
        CommandRejected.TryRead(rejected.Payload, out CommandRejected refusal);
        Assert.That(refusal.CommandSequence, Is.EqualTo(1u));
        Assert.That(refusal.Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
    }

    [Test]
    public void Logout_WritesTheCheckpointThenLeavesTheWorldAndListsTheCharacters()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.Place(connection, Stored.X, Stored.Z);
        server.Transport.ClearSent();

        server.SendLogout(connection, 1);
        server.TickUntil(() => server.Transport.ControlOpcodesSentTo(connection).Contains(MessageOpcode.CharacterList));

        Assert.That(
            server.Transport.ControlOpcodesSentTo(connection),
            Is.EqualTo(new[] { MessageOpcode.LogoutComplete, MessageOpcode.CharacterList }));
        Assert.That(server.Store.Checkpoints.Single().Position.X, Is.EqualTo(Stored.X));
        Assert.That(server.SessionOf(connection).State, Is.EqualTo(SessionState.Authenticated));
        Assert.That(server.SessionOf(connection).Character, Is.Null);
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Transport.Disconnects, Is.Empty, "the connection stays open");
    }
}
}
