using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Picking up a drop: the checks in their order, the reservation, and the commit before anyone is told
///     (Gameplay Systems §11, §13; Persistence §5; Network Protocol §9, §11).
/// </summary>
[TestFixture]
public sealed class PickupTests
{
    private const string SlimeGel = "item.material.slime_gel";
    private const int StackLimit = 999;
    private const int PriorityTicks = PickupSystem.LootPriorityMs * TestServer.TickRate / 1000;

    private static WorldPosition NextTo(PlayerEntity player, float dx)
    {
        return new WorldPosition(player.Position.X + dx, player.Position.Y, player.Position.Z);
    }

    private static ItemDropEntity Drop(
        TestServer server,
        WorldPosition at,
        CharacterId priority = default,
        uint amount = 2,
        long expiresAtMs = long.MaxValue)
    {
        MapInstance map = server.World.Maps.Single();
        ItemDropEntity drop = server.World.SpawnItemDrop(
            map,
            new ItemDefinitionId(SlimeGel),
            amount,
            at,
            server.CurrentTick,
            expiresAtMs,
            priority);
        server.Tick();
        return drop;
    }

    private static List<T> Received<T>(TestServer server, ConnectionId connection, MessageOpcode opcode,
        ReadMessage<T> read)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload))
            .ToList();
    }

    private delegate T ReadMessage<out T>(byte[] payload);

    private static List<CommandRejected> Rejections(TestServer server, ConnectionId connection)
    {
        return Received(server, connection, MessageOpcode.CommandRejected, payload =>
        {
            CommandRejected.TryRead(payload, out CommandRejected rejected);
            return rejected;
        });
    }

    private static List<ItemPickedUp> PickedUp(TestServer server, ConnectionId connection)
    {
        return Received(server, connection, MessageOpcode.ItemPickedUp, payload =>
        {
            ItemPickedUp.TryRead(payload, out ItemPickedUp? message);
            return message!;
        });
    }

    private static List<InventoryChanged> Changes(TestServer server, ConnectionId connection)
    {
        return Received(server, connection, MessageOpcode.InventoryChanged, payload =>
        {
            InventoryChanged.TryRead(payload, out InventoryChanged? message);
            return message!;
        });
    }

    private static CommandRejectionReason RefusalOf(TestServer server, ConnectionId connection, EntityId drop,
        uint sequence)
    {
        server.Transport.ClearSent();
        server.SendPickup(connection, drop, sequence);
        server.Tick(3);
        CommandRejected rejected = Rejections(server, connection).Single();
        Assert.That(rejected.CommandSequence, Is.EqualTo(sequence));
        return rejected.Reason;
    }

    private static ConnectionId EnterHolding(TestServer server, long character, int quantity)
    {
        ConnectionId connection = server.Connect();
        server.SignInWithCharacter(connection, character);
        server.Store.GiveItems(character, SlimeGel, 1, quantity, 5);
        server.SendEnterWorld(connection, character);
        server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
        return connection;
    }

    [Test]
    public void Commit_ThatNeverHappened_ReleasesTheDropAsServiceUnavailableOnceTheLedgerAnswers()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.Transport.ClearSent();
        server.SendPickup(picker, drop.Id, 1);
        server.Store.IsUnavailable = true;
        server.Tick(3);

        Assert.That(drop.IsReserved, Is.True, "the ledger cannot be asked yet");

        server.Store.IsUnavailable = false;
        server.TickUntil(() => !drop.IsReserved);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True);
        Assert.That(server.Store.LedgerCount, Is.Zero);
        CommandRejected rejected = Rejections(server, picker).Single();
        Assert.That(rejected.CommandSequence, Is.EqualTo(1u));
        Assert.That(rejected.Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));
        Assert.That(PickedUp(server, picker), Is.Empty);
        Assert.That(Changes(server, picker), Is.Empty);
    }

    [Test]
    public void Commit_WhoseAnswerIsLost_IsSettledFromTheLedgerExactlyOnce()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.Store.AmbiguousPickupFailures = 100;
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True, "no answer yet, so nothing is announced");
        Assert.That(drop.IsReserved, Is.True);
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));

        server.Store.AmbiguousPickupFailures = 0;
        server.TickUntil(() => !server.World.Maps.Single().Contains(drop.Id));

        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(2));
        Assert.That(PickedUp(server, picker), Has.Count.EqualTo(1));
        Assert.That(Changes(server, picker).Single().NewRevision, Is.EqualTo(1u));
        Assert.That(Rejections(server, picker), Is.Empty);
    }

    [Test]
    public void Disconnect_WithAPickupInFlightAndNoGrace_RemovesTheCharacterOnlyAfterItSettles()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.Tick();
        server.Disconnect(picker);
        server.Tick();

        Assert.That(server.World.Maps.Single().Players, Has.Count.EqualTo(1));

        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That(server.World.Maps.Single().Players, Is.Empty);
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(2));
        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(1));
    }

    [Test]
    public void Drop_ThatExpiresWhileReserved_StaysUntilThePickupFailsAndThenExpires()
    {
        var server = new TestServer();
        ConnectionId picker = EnterHolding(server, 1, StackLimit);
        long soon = (long)server.CurrentTick * 1000 / TestServer.TickRate + 100;
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f), expiresAtMs: soon);
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.Tick(10);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True, "a reserved drop does not expire");

        server.RunsPersistence = true;
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False, "released, it expires on the next pass");
        Assert.That(Rejections(server, picker).Last().Reason, Is.EqualTo(CommandRejectionReason.InventoryFull));
    }

    [Test]
    public void Logout_AfterAnEarlierLogoutsCheckpointFailed_IsNotFinishedByThatOldCheckpoint()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.RunsPersistence = false;
        server.SendLogout(picker, 1);
        server.Tick();
        server.Store.IsUnavailable = true;
        server.RunsPersistence = true;
        server.Tick();
        Assert.That(Rejections(server, picker).Single().Reason, Is.EqualTo(CommandRejectionReason.ServiceUnavailable));

        // The failed checkpoint waits in its slot for the database; a pickup and a second logout come before it runs.
        server.Store.IsUnavailable = false;
        server.RunsPersistence = false;
        server.Persistence.Probe();
        server.SendPickup(picker, drop.Id, 2);
        server.SendLogout(picker, 3);
        server.Tick();
        server.RunsPersistence = true;
        server.TickUntil(() => server.SessionOf(picker).State == SessionState.Authenticated);

        MessageOpcode[] opcodes = server.Transport.ControlOpcodesSentTo(picker).ToArray();
        Assert.That(opcodes, Does.Contain(MessageOpcode.InventoryChanged), "the pickup reached its player first");
        Assert.That(
            Array.IndexOf(opcodes, MessageOpcode.InventoryChanged),
            Is.LessThan(Array.IndexOf(opcodes, MessageOpcode.LogoutComplete)));
        Assert.That(server.Store.Checkpoints, Has.Count.EqualTo(2), "the old checkpoint, then the logout's own");
    }

    [Test]
    public void Logout_WaitingForAPickupWhoseCommitMeetsAnOutage_IsCancelledAndTheCharacterPlaysOn()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.SendPickup(picker, drop.Id, 1);
        server.SendLogout(picker, 2);
        server.Tick();
        server.Store.IsUnavailable = true;

        server.Tick(2);

        Assert.That(server.SessionOf(picker).Character!.IsLoggingOut, Is.False);
        Assert.That(
            Rejections(server, picker).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EqualTo(new[] { (2u, CommandRejectionReason.ServiceUnavailable) }),
            "the logout is answered; the pickup stays unsettled until the ledger answers");

        server.Store.IsUnavailable = false;
        server.TickUntil(() => server.SessionOf(picker).Character!.Operation == null);
        server.Tick(2);

        Assert.That(server.SessionOf(picker).State, Is.EqualTo(SessionState.InWorld));
        Assert.That(Rejections(server, picker).Last().CommandSequence, Is.EqualTo(1u));
        Assert.That(server.Transport.ControlOpcodesSentTo(picker), Has.None.EqualTo(MessageOpcode.LogoutComplete));
    }

    [Test]
    public void Logout_WithAPickupInFlight_WaitsForItThenCompletes()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.RunsPersistence = false;
        server.SendPickup(picker, drop.Id, 1);
        server.SendLogout(picker, 2);
        server.Tick(3);

        Assert.That(server.Store.Checkpoints, Is.Empty, "the logout checkpoint waits for the pickup");

        server.RunsPersistence = true;
        server.TickUntil(() => server.SessionOf(picker).State == SessionState.Authenticated);

        MessageOpcode[] opcodes = server.Transport.ControlOpcodesSentTo(picker).ToArray();
        Assert.That(
            Array.IndexOf(opcodes, MessageOpcode.InventoryChanged),
            Is.LessThan(Array.IndexOf(opcodes, MessageOpcode.LogoutComplete)));
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(2));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
    }

    [Test]
    public void Pickup_AtTheEdgeOfRangeAndTolerance_IsAccepted()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1.99f));

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }

    [Test]
    public void Pickup_BeforeItsCommitAnswers_TellsNobodyAndKeepsTheDropReserved()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.RunsPersistence = false;
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(5);

        Assert.That(server.Transport.ControlSentTo(picker), Is.Empty);
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True);
        Assert.That(drop.ReservedBy, Is.EqualTo(new CharacterId(1)));
        Assert.That(server.Store.LedgerCount, Is.Zero);
    }

    [Test]
    public void Pickup_InRange_IsCommittedThenAnnouncedToEveryoneWhoKnowsTheDrop()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ConnectionId watcher = server.EnterWorld(2);
        PlayerEntity player = server.PlayerOf(picker);
        ItemDropEntity drop = Drop(server, NextTo(player, 1f), amount: 3);
        server.Transport.ClearSent();

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
        Assert.That(server.Store.LedgerCount, Is.EqualTo(1));
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(3));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(picker),
            Is.EqualTo(new[]
            {
                MessageOpcode.ItemPickedUp, MessageOpcode.EntityDespawn, MessageOpcode.InventoryChanged
            }));
        ItemPickedUp announced = PickedUp(server, picker).Single();
        Assert.That(announced.Drop, Is.EqualTo(drop.Id));
        Assert.That(announced.Recipient, Is.EqualTo(player.Id));
        Assert.That(announced.Amount, Is.EqualTo(3u));
        InventoryChanged change = Changes(server, picker).Single();
        Assert.That(change.PriorRevision, Is.EqualTo(0u));
        Assert.That(change.NewRevision, Is.EqualTo(1u));
        Assert.That(change.Changes.Single().Quantity, Is.EqualTo(3u));
        server.Sessions.TryGetCharacter(new CharacterId(1), out CharacterSession? character);
        Assert.That(character!.Inventory.Revision, Is.EqualTo(1u));
        Assert.That(character.Inventory.Rows.Single().Quantity, Is.EqualTo(3u));

        Assert.That(
            server.Transport.ControlOpcodesSentTo(watcher),
            Is.EqualTo(new[] { MessageOpcode.ItemPickedUp, MessageOpcode.EntityDespawn }));
        Assert.That(PickedUp(server, watcher).Single().Recipient, Is.EqualTo(player.Id));
        EntityDespawn.TryRead(
            server.Transport.ControlSentTo(watcher).Last().Payload,
            out EntityDespawn despawn);
        Assert.That(despawn.Reason, Is.EqualTo(DespawnReason.PickedUp));
    }

    [Test]
    public void Pickup_OfADropAnotherPickupReserved_IsRejectedAsBusy()
    {
        var server = new TestServer();
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(first), 1f));
        server.RunsPersistence = false;
        server.SendPickup(first, drop.Id, 1);
        server.Tick();

        Assert.That(RefusalOf(server, second, drop.Id, 1), Is.EqualTo(CommandRejectionReason.Busy));
    }

    [Test]
    public void Pickup_OfAMissingEntityOrANonDrop_IsRejectedAsInvalidTarget()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);

        Assert.That(RefusalOf(server, picker, new EntityId(9999), 1), Is.EqualTo(CommandRejectionReason.InvalidTarget));
        Assert.That(RefusalOf(server, picker, server.PlayerOf(other).Id, 2),
            Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Pickup_OfAnotherCharactersDropAfterItsPriorityWindow_IsAccepted()
    {
        var server = new TestServer();
        server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(other), 1f), new CharacterId(1));
        server.Tick(PriorityTicks);

        server.SendPickup(other, drop.Id, 1);
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
        Assert.That(server.Store.Stored(2).Items.Single().Quantity, Is.EqualTo(2));
    }

    [Test]
    public void Pickup_OfAnotherCharactersDropInsideItsPriorityWindow_IsRejectedAsLootPriority()
    {
        var server = new TestServer();
        ConnectionId owner = server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(other), 1f), new CharacterId(1));

        Assert.That(RefusalOf(server, other, drop.Id, 1), Is.EqualTo(CommandRejectionReason.LootPriority));
        Assert.That(server.Sessions.TryGet(owner, out _), Is.True);
    }

    [Test]
    public void Pickup_OfItsOwnPriorityDrop_IsAcceptedAtOnce()
    {
        var server = new TestServer();
        ConnectionId owner = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(owner), 1f), new CharacterId(1));

        server.SendPickup(owner, drop.Id, 1);
        server.Tick(2);

        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.False);
    }

    [Test]
    public void Pickup_OutOfRange_IsRejectedAsOutOfRange()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 2.1f));

        Assert.That(RefusalOf(server, picker, drop.Id, 1), Is.EqualTo(CommandRejectionReason.OutOfRange));
        Assert.That(drop.IsReserved, Is.False);
        Assert.That(server.Store.PickupCommits, Is.Empty);
    }

    [Test]
    public void Pickup_ThatWouldPassTheStackLimit_IsRejectedAsFullAndLeavesTheDrop()
    {
        var server = new TestServer();
        ConnectionId picker = EnterHolding(server, 1, StackLimit - 1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f), amount: 2);

        Assert.That(RefusalOf(server, picker, drop.Id, 1), Is.EqualTo(CommandRejectionReason.InventoryFull));
        Assert.That(server.World.Maps.Single().Contains(drop.Id), Is.True);
        Assert.That(drop.IsReserved, Is.False);
        Assert.That(server.Store.Stored(1).Items.Single().Quantity, Is.EqualTo(StackLimit - 1));
        Assert.That(server.Store.LedgerCount, Is.Zero);
    }

    [Test]
    public void Pickup_WhileDead_IsRejectedAsNotAllowedNow()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity drop = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        server.Combat.Kill(server.World.Maps.Single(), server.PlayerOf(picker), null, server.CurrentTick);

        Assert.That(RefusalOf(server, picker, drop.Id, 1), Is.EqualTo(CommandRejectionReason.NotAllowedNow));
    }

    [Test]
    public void Pickup_WhileItsOwnPickupIsInFlight_IsRejectedAsBusy()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        ItemDropEntity first = Drop(server, NextTo(server.PlayerOf(picker), 1f));
        ItemDropEntity second = Drop(server, NextTo(server.PlayerOf(picker), -1f));
        server.RunsPersistence = false;
        server.SendPickup(picker, first.Id, 1);
        server.Tick();

        Assert.That(RefusalOf(server, picker, second.Id, 2), Is.EqualTo(CommandRejectionReason.Busy));
        Assert.That(second.IsReserved, Is.False);
    }
}
}
