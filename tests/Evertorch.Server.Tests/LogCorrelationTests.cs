using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Logs carry the connection, account, character, and operation ID as fields, so one player's or one job's story
///     can be followed through the log (System Architecture §10, Persistence §9).
/// </summary>
[TestFixture]
public sealed class LogCorrelationTests
{
    private static IReadOnlyDictionary<string, object?> FieldsOf<T>(CapturingLogger<T> log, string eventName)
    {
        return log.Entries.Single(entry => entry.EventId.Name == eventName).Fields;
    }

    private static PersistenceWorker CreateWorker(CapturingLogger<PersistenceWorker> log)
    {
        return new PersistenceWorker(
            new InMemoryGameStore(),
            Options.Create(new PersistenceOptions()),
            TestInstruments.Create(),
            log);
    }

    // A job that fails for good: any exception but the store's own unavailability is not retried.
    private static PersistenceJob<bool> FailingJob(
        string operation,
        ConnectionId connection = default,
        long character = 0,
        string? operationId = null)
    {
        return new PersistenceJob<bool>(
            operation,
            connection,
            character,
            (_, _) => throw new InvalidOperationException("a programming error"),
            IgnoreCompletion,
            operationId);
    }

    private static void IgnoreCompletion(PersistenceOutcome outcome, bool value)
    {
    }

    [Test]
    public void CheckpointFailed_DuringAnOutage_NamesTheCheckpointJobAndItsConnection()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.Store.IsUnavailable = true;

        server.Lifetime.QueueCheckpoint(server.SessionOf(connection).Character!);
        server.Tick(2);

        IReadOnlyDictionary<string, object?> fields = FieldsOf(server.LifetimeLog, "CheckpointFailed");
        Assert.That(fields["OperationId"], Is.Not.Null.And.Not.Empty);
        Assert.That(fields["Character"], Is.EqualTo(1L));
        Assert.That(fields["Connection"], Is.EqualTo(connection.Value));
    }

    [Test]
    public void OperationId_WithoutOneGiven_IsTheJobIdInQueueOrder()
    {
        var log = new CapturingLogger<PersistenceWorker>();
        using PersistenceWorker worker = CreateWorker(log);
        PersistenceJob<bool> first = FailingJob("first");
        PersistenceJob<bool> second = FailingJob("second");
        PersistenceJob<bool> pickup = FailingJob("pickup", operationId: "a-drop");

        worker.TryEnqueue(first);
        worker.TryEnqueue(second);
        worker.TryEnqueue(pickup);

        Assert.That(first.OperationId, Is.EqualTo("1"));
        Assert.That(second.OperationId, Is.EqualTo("2"));
        Assert.That(pickup.Id, Is.EqualTo(3));
        Assert.That(pickup.OperationId, Is.EqualTo("a-drop"), "a pickup is named by its drop");
    }

    [Test]
    public void PersistenceJobFailed_NamesTheOperationIdCharacterAndConnection()
    {
        var log = new CapturingLogger<PersistenceWorker>();
        using PersistenceWorker worker = CreateWorker(log);
        PersistenceJob<bool> job = FailingJob("pickup", new ConnectionId(7), 42, "the-drop");

        worker.TryEnqueue(job);
        worker.RunUntilIdle();

        IReadOnlyDictionary<string, object?> fields = FieldsOf(log, "PersistenceJobFailed");
        Assert.That(fields["Operation"], Is.EqualTo("pickup"));
        Assert.That(fields["OperationId"], Is.EqualTo("the-drop"));
        Assert.That(fields["Character"], Is.EqualTo(42L));
        Assert.That(fields["Connection"], Is.EqualTo(7L));
    }

    [Test]
    public void PickupUnsettled_NamesTheDropAsTheOperationAndTheConnection()
    {
        var server = new TestServer();
        ConnectionId picker = server.EnterWorld(1);
        PlayerEntity player = server.PlayerOf(picker);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            server.World.Maps.Single(),
            new ItemDefinitionId("item.material.slime_gel"),
            1,
            new WorldPosition(player.Position.X + 1f, player.Position.Y, player.Position.Z),
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.Store.AmbiguousPickupFailures = 100;

        server.SendPickup(picker, drop.Id, 1);
        server.Tick(2);

        IReadOnlyDictionary<string, object?> fields = FieldsOf(server.PickupLog, "PickupUnsettled");
        Assert.That(fields["OperationId"], Is.EqualTo(drop.DropId));
        Assert.That(fields["Character"], Is.EqualTo(1L));
        Assert.That(fields["Connection"], Is.EqualTo(picker.Value));
    }

    [Test]
    public void SessionClosed_ForACharacterInTheWorld_NamesItsConnectionAccountAndCharacter()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorldAs("correlation", "Correl1");
        ClientSession session = server.SessionOf(connection);
        long account = session.Account!.Value.Value;
        long character = session.Character!.Character.Value;

        server.Disconnect(connection);
        server.Tick();

        IReadOnlyDictionary<string, object?> fields = FieldsOf(server.Log, "SessionClosed");
        Assert.That(fields["Connection"], Is.EqualTo(connection.Value));
        Assert.That(fields["Account"], Is.EqualTo(account));
        Assert.That(fields["Character"], Is.EqualTo(character));
    }

    [Test]
    public void WorldEntered_NamesTheAccountBehindTheConnection()
    {
        var server = new TestServer();

        ConnectionId connection = server.EnterWorldAs("correlation", "Correl2");

        IReadOnlyDictionary<string, object?> fields = FieldsOf(server.Log, "WorldEntered");
        Assert.That(fields["Connection"], Is.EqualTo(connection.Value));
        Assert.That(fields["Account"], Is.EqualTo(server.SessionOf(connection).Account!.Value.Value));
    }
}
}
