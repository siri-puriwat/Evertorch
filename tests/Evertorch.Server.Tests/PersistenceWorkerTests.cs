using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The bridge between the tick thread and the database (Persistence §9), with the in-memory store.
/// </summary>
[TestFixture]
public sealed class PersistenceWorkerTests
{
    private static PersistenceWorker CreateWorker(
        InMemoryGameStore store,
        int capacity = 256,
        int timeoutMs = 5000,
        int maxRetries = 3,
        CapturingLogger<PersistenceWorker>? logger = null)
    {
        var options = new PersistenceOptions
        {
            QueueCapacity = capacity,
            CommandTimeoutMs = timeoutMs,
            MaxRetries = maxRetries,
            RetryBaseDelayMs = 1
        };
        return new PersistenceWorker(
            store,
            Options.Create(options),
            logger ?? new CapturingLogger<PersistenceWorker>());
    }

    private static PersistenceJob<int> Job(
        string name,
        List<string> log,
        long character = 0,
        Func<int>? work = null)
    {
        return new PersistenceJob<int>(
            name,
            default,
            character,
            (_, _) =>
            {
                log.Add($"run {name}");
                return Task.FromResult(work?.Invoke() ?? 0);
            },
            (outcome, _) => log.Add($"done {name} {outcome}"));
    }

    private static Task<bool> StopInBackground(PersistenceWorker worker)
    {
        return Task.Run(() => worker.Stop(5000));
    }

    private static void CompleteAll(PersistenceWorker worker)
    {
        while (worker.TryDequeueCompletion(out PersistenceJob job))
        {
            job.Complete();
        }
    }

    [Test]
    public void Checkpoint_ReplacedBeforeItRuns_OnlyTheNewestIsWritten()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        worker.QueueCheckpoint(Job("old", log, 7));
        worker.QueueCheckpoint(Job("new", log, 7));

        worker.RunUntilIdle();

        Assert.That(log, Is.EqualTo(new[] { "run new" }));
        Assert.That(worker.WaitingCheckpoints, Is.Zero);
    }

    [Test]
    public void Checkpoint_ThatCannotReachTheDatabase_IsRetriedOnceItAnswers()
    {
        var store = new InMemoryGameStore();
        using PersistenceWorker worker = CreateWorker(store, maxRetries: 0);
        var log = new List<string>();
        bool isDown = true;
        worker.QueueCheckpoint(Job("checkpoint", log, 7, () =>
        {
            if (isDown)
            {
                store.IsUnavailable = true;
                throw new StoreUnavailableException(new TimeoutException());
            }

            return 0;
        }));

        worker.RunUntilIdle();
        CompleteAll(worker);
        int waitingDuringOutage = worker.WaitingCheckpoints;
        isDown = false;
        store.IsUnavailable = false;
        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(waitingDuringOutage, Is.EqualTo(1));
        Assert.That(worker.WaitingCheckpoints, Is.Zero);
        Assert.That(
            log,
            Is.EqualTo(
                new[]
                {
                    "run checkpoint", "done checkpoint Unavailable", "run checkpoint", "done checkpoint Succeeded"
                }));
    }

    [Test]
    public void Checkpoints_DoNotTakeQueuePlaces()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore(), 1);
        var log = new List<string>();
        worker.QueueCheckpoint(Job("checkpoint 7", log, 7));
        worker.QueueCheckpoint(Job("checkpoint 8", log, 8));

        Assert.That(worker.TryEnqueue(Job("a", log)), Is.True);
    }

    [Test]
    public void Completion_IsAppliedInDrainCommandsBeforeTheTicksNetworkCommands()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(1);
        var order = new List<string>();
        server.Persistence.TryEnqueue(
            new PersistenceJob<int>(
                "probe",
                player,
                0,
                (_, _) => Task.FromResult(0),
                (_, _) => order.Add($"completion, session in world: {server.SessionOf(player).State}")));
        server.Disconnect(player);

        server.Tick();

        Assert.That(order, Is.EqualTo(new[] { "completion, session in world: InWorld" }));
        Assert.That(server.Sessions.TryGet(player, out _), Is.False, "the disconnect was handled after it");
    }

    [Test]
    public void Completion_ThatThrows_ClosesOnlyItsOwnConnection()
    {
        var server = new TestServer();
        ConnectionId faulty = server.EnterWorld(1);
        ConnectionId other = server.EnterWorld(2);
        server.Persistence.TryEnqueue(
            new PersistenceJob<int>(
                "faulty",
                faulty,
                0,
                (_, _) => Task.FromResult(0),
                (_, _) => throw new InvalidOperationException("boom")));

        server.Tick();

        Assert.That(server.Transport.Disconnects[faulty], Is.EqualTo(DisconnectReason.InternalError));
        Assert.That(server.Transport.Disconnects.ContainsKey(other), Is.False);
        Assert.That(server.SessionOf(other).State, Is.EqualTo(SessionState.InWorld));
    }

    [Test]
    public void Job_ForACharacter_RunsThatCharactersWaitingCheckpointFirst()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        worker.TryEnqueue(Job("other", log, 8));
        worker.TryEnqueue(Job("load", log, 7));
        worker.QueueCheckpoint(Job("checkpoint", log, 7));

        worker.RunUntilIdle();

        Assert.That(log, Is.EqualTo(new[] { "run other", "run checkpoint", "run load" }));
    }

    [Test]
    public void Job_ThatFailsForAnotherReason_IsNotRetried()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        int attempts = 0;
        worker.TryEnqueue(Job("a", log, work: () =>
        {
            attempts++;
            throw new InvalidOperationException("constraint");
        }));

        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(log[^1], Is.EqualTo("done a Failed"));
        Assert.That(worker.State, Is.Not.EqualTo(DatabaseState.Unavailable));
    }

    [Test]
    public void Job_ThatOutlivesTheCommandTimeout_IsCancelledAndUnavailable()
    {
        var store = new InMemoryGameStore { IsUnavailable = true };
        using PersistenceWorker worker = CreateWorker(store, timeoutMs: 200);
        var log = new List<string>();
        var job = new PersistenceJob<int>(
            "slow",
            default,
            0,
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            },
            (outcome, _) => log.Add(outcome.ToString()));
        worker.TryEnqueue(job);

        var elapsed = Stopwatch.StartNew();
        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(log, Is.EqualTo(new[] { "Unavailable" }));
        Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(3000));
    }

    [Test]
    public void Job_WhoseDatabaseNeverAnswers_IsRetriedMaxRetriesTimes_ThenUnavailable()
    {
        var store = new InMemoryGameStore();
        using PersistenceWorker worker = CreateWorker(store, maxRetries: 3);
        var log = new List<string>();
        int attempts = 0;
        worker.TryEnqueue(Job("a", log, work: () =>
        {
            attempts++;
            store.IsUnavailable = true;
            throw new StoreUnavailableException(new TimeoutException());
        }));

        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(attempts, Is.EqualTo(4));
        Assert.That(log[^1], Is.EqualTo("done a Unavailable"));
        Assert.That(worker.State, Is.EqualTo(DatabaseState.Unavailable));
    }

    [Test]
    public void Job_WhoseDatabaseRecoversOnTheSecondAttempt_Succeeds()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        int attempts = 0;
        worker.TryEnqueue(Job("a", log, work: () =>
        {
            attempts++;
            return attempts == 1 ? throw new StoreUnavailableException(new TimeoutException()) : 1;
        }));

        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(attempts, Is.EqualTo(2));
        Assert.That(worker.Retries, Is.EqualTo(1));
        Assert.That(log[^1], Is.EqualTo("done a Succeeded"));
        Assert.That(worker.State, Is.EqualTo(DatabaseState.Available));
    }

    [Test]
    public void Jobs_QueuedBehindOneThatMeetsAnOutage_EndUnavailableWithoutRunning()
    {
        var store = new InMemoryGameStore();
        using PersistenceWorker worker = CreateWorker(store, maxRetries: 0);
        var log = new List<string>();
        worker.TryEnqueue(Job("first", log, work: () =>
        {
            store.IsUnavailable = true;
            throw new StoreUnavailableException(new TimeoutException());
        }));
        worker.TryEnqueue(Job("second", log));
        worker.QueueCheckpoint(Job("checkpoint", log, 7));

        worker.RunUntilIdle();
        CompleteAll(worker);

        Assert.That(log, Is.EqualTo(new[] { "run first", "done first Unavailable", "done second Unavailable" }));
        Assert.That(worker.PendingJobs, Is.Zero);
        Assert.That(worker.WaitingCheckpoints, Is.EqualTo(1), "a checkpoint waits for the database");
    }

    [Test]
    public void Jobs_RunInTheOrderQueued_AndCompleteOnlyWhenTheTickThreadAsks()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        worker.TryEnqueue(Job("a", log));
        worker.TryEnqueue(Job("b", log));

        worker.RunUntilIdle();
        var afterRun = new List<string>(log);
        CompleteAll(worker);

        Assert.That(afterRun, Is.EqualTo(new[] { "run a", "run b" }));
        Assert.That(log, Is.EqualTo(new[] { "run a", "run b", "done a Succeeded", "done b Succeeded" }));
    }

    [Test]
    public void Outage_WhoseProbeMeetsAnErrorOfTheDatabasesOwn_KeepsTheWriterAliveAndUnavailable()
    {
        var store = new InMemoryGameStore { IsUnavailable = true };
        var logger = new CapturingLogger<PersistenceWorker>();
        using PersistenceWorker worker = CreateWorker(store, logger: logger);
        var log = new List<string>();
        worker.Probe();
        worker.QueueCheckpoint(Job("checkpoint", log, 7));
        store.IsUnavailable = false;
        store.MigrationQueryFailure = new InvalidOperationException("password authentication failed");

        Action writerProbesTwice = () =>
        {
            worker.RunUntilIdle();
            worker.RunUntilIdle();
        };
        Action startupProbe = () => worker.Probe();

        Assert.That(writerProbesTwice, Throws.Nothing, "on the writer thread an escaped exception ends the process");
        Assert.That(worker.State, Is.EqualTo(DatabaseState.Unavailable));
        Assert.That(log, Is.Empty, "nothing is written while the probe fails");
        Assert.That(
            logger.Entries.Count(entry => entry.EventId.Name == "DatabaseProbeFailed"),
            Is.EqualTo(1),
            "once per outage, not once a second");
        Assert.That(startupProbe, Throws.InstanceOf<InvalidOperationException>(), "startup still stops on it");

        store.MigrationQueryFailure = null;
        worker.RunUntilIdle();

        Assert.That(log, Is.EqualTo(new[] { "run checkpoint" }), "the writer goes on once a probe passes");
    }

    [Test]
    public void Probe_FindingPendingMigrations_KeepsTheServerUnreadyAndRunsNothing()
    {
        var store = new InMemoryGameStore { PendingMigrations = new[] { "20990101000000_Later" } };
        using PersistenceWorker worker = CreateWorker(store);
        var log = new List<string>();
        worker.QueueCheckpoint(Job("checkpoint", log, 7));

        IReadOnlyList<string> pending = worker.Probe();
        worker.RunUntilIdle();

        Assert.That(pending, Is.EqualTo(new[] { "20990101000000_Later" }));
        Assert.That(worker.State, Is.EqualTo(DatabaseState.PendingMigrations));
        Assert.That(worker.IsAvailable, Is.False);
        Assert.That(worker.TryEnqueue(Job("a", log)), Is.False);
        Assert.That(log, Is.Empty, "a schema this build does not know is never written to");
    }

    [Test]
    public void Probe_WhenTheDatabaseAnswersAgain_MakesItAvailable()
    {
        var store = new InMemoryGameStore { IsUnavailable = true };
        using PersistenceWorker worker = CreateWorker(store);
        worker.Probe();

        store.IsUnavailable = false;
        worker.Probe();

        Assert.That(worker.State, Is.EqualTo(DatabaseState.Available));
        Assert.That(worker.TryEnqueue(Job("a", new List<string>())), Is.True);
    }

    [Test]
    public void RetryDelay_StaysWithinTheDoublingBound()
    {
        var random = new Random(1);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int bound = 100 << attempt;
            for (int draw = 0; draw < 200; draw++)
            {
                Assert.That(PersistenceWorker.RetryDelayMs(attempt, 100, random), Is.InRange(0, bound));
            }
        }
    }

    [Test]
    public void Stop_WhileTheWriterMovesOnToTheLastCheckpoint_WaitsForItToBeWritten()
    {
        // Many rounds, because the moment Stop must not mistake for the end of the drain is short: the writer has
        // just taken the last checkpoint from the queue and not yet started it.
        for (int round = 0; round < 200; round++)
        {
            using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
            using var release = new ManualResetEventSlim();
            PersistenceOutcome? last = null;
            worker.Start();
            worker.QueueCheckpoint(new PersistenceJob<int>(
                "first",
                default,
                1,
                (_, _) =>
                {
                    release.Wait();
                    return Task.FromResult(0);
                },
                (_, _) =>
                {
                }));
            worker.QueueCheckpoint(new PersistenceJob<int>(
                "last",
                default,
                2,
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(0);
                },
                (outcome, _) => last = outcome));
            Task<bool> stopping = StopInBackground(worker);
            Thread.Sleep(2);

            release.Set();

            Assert.That(stopping.Result, Is.True, $"round {round}");
            CompleteAll(worker);
            Assert.That(last, Is.EqualTo(PersistenceOutcome.Succeeded), $"round {round}");
        }
    }

    [Test]
    public void Stop_WithAJobStillRunning_GivesUpAfterTheDrainTimeout()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore(), timeoutMs: 60000);
        var job = new PersistenceJob<int>(
            "stuck",
            default,
            0,
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            },
            (_, _) =>
            {
            });
        worker.Start();
        worker.TryEnqueue(job);
        worker.TryEnqueue(Job("behind", new List<string>()));

        var elapsed = Stopwatch.StartNew();
        bool isDrained = worker.Stop(300);

        Assert.That(isDrained, Is.False);
        Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(5000));
    }

    [Test]
    public void Stop_WithQuickJobs_DrainsThemAll()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore());
        var log = new List<string>();
        worker.Start();
        for (int index = 0; index < 20; index++)
        {
            worker.TryEnqueue(Job($"job{index}", log));
        }

        bool isDrained = worker.Stop(5000);

        Assert.That(isDrained, Is.True);
        Assert.That(log, Has.Count.EqualTo(20));
    }

    [Test]
    public void TryEnqueue_WhenTheQueueIsFull_RefusesWithoutQueueing()
    {
        using PersistenceWorker worker = CreateWorker(new InMemoryGameStore(), 2);
        var log = new List<string>();

        bool first = worker.TryEnqueue(Job("a", log));
        bool second = worker.TryEnqueue(Job("b", log));
        bool third = worker.TryEnqueue(Job("c", log));
        worker.RunUntilIdle();

        Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { true, true, false }));
        Assert.That(log, Is.EqualTo(new[] { "run a", "run b" }));
    }

    [Test]
    public void TryEnqueue_WhileTheDatabaseIsUnavailable_RefusesAtOnce()
    {
        var store = new InMemoryGameStore { IsUnavailable = true };
        using PersistenceWorker worker = CreateWorker(store);
        worker.Probe();
        var log = new List<string>();

        bool isQueued = worker.TryEnqueue(Job("a", log));

        Assert.That(worker.State, Is.EqualTo(DatabaseState.Unavailable));
        Assert.That(isQueued, Is.False);
        Assert.That(worker.PendingJobs, Is.Zero);
    }
}
}
