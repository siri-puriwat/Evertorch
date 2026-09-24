using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Evertorch.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The bridge between the tick thread and the database (Persistence §9, System Architecture §8). The tick thread
///     hands jobs over without blocking; one writer thread runs them in order, one unit of work each, so a
///     character's jobs never overtake one another; results come back through a completion queue that the
///     <c>DrainCommands</c> phase empties.
/// </summary>
/// <remarks>
///     Checkpoints do not take queue places. Each character has one checkpoint slot, and a newer checkpoint replaces
///     one still waiting there. A checkpoint that cannot reach the database goes back into its slot and is retried
///     once the database answers again, so its completion can run more than once. A job for a character runs that
///     character's waiting checkpoint first, so a load never reads a position older than one already handed over.
/// </remarks>
public sealed class PersistenceWorker : IDisposable
{
    public const int ProbeIntervalMs = 1000;

    private static readonly Action<ILogger, string, string, long, long, Exception?> LogJobFailed =
        LoggerMessage.Define<string, string, long, long>(
            LogLevel.Error,
            new EventId(4004, "PersistenceJobFailed"),
            "The {Operation} job {OperationId} for character {Character} on connection {Connection} failed.");

    private static readonly Action<ILogger, Exception?> LogDatabaseUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4002, "DatabaseUnavailable"),
            "The database cannot be reached; durable commands and admission are refused until it answers.");

    private static readonly Action<ILogger, Exception?> LogDatabaseAvailable =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(4003, "DatabaseReady"),
            "The database is reachable and its schema is current.");

    private static readonly Action<ILogger, int, string, Exception?> LogMigrationsPending =
        LoggerMessage.Define<int, string>(
            LogLevel.Critical,
            new EventId(4001, "MigrationsPending"),
            "The database has {Count} pending migration(s): {Migrations}. Apply them with scripts/db-migrate.ps1.");

    private static readonly Action<ILogger, int, Exception?> LogDrainIncomplete =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(4005, "PersistenceDrainIncomplete"),
            "{Count} persistence job(s) were still waiting when the shutdown drain ended.");

    private static readonly Action<ILogger, Exception?> LogProbeFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(4009, "DatabaseProbeFailed"),
            "The database answered the availability probe with an error; it stays unavailable until a probe passes.");

    private readonly IGameStore m_store;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<PersistenceWorker> m_logger;
    private readonly int m_capacity;
    private readonly int m_timeoutMs;
    private readonly int m_maxRetries;
    private readonly int m_retryBaseDelayMs;
    private readonly object m_gate = new();
    private readonly Queue<Entry> m_queue = new();
    private readonly Dictionary<long, PersistenceJob> m_checkpoints = new();
    private readonly ConcurrentQueue<PersistenceJob> m_completions = new();
    private readonly CancellationTokenSource m_stop = new();

    // Jitter only spreads retries; it never decides a gameplay outcome, so it does not use the seeded server random.
    private readonly Random m_jitter = new();
    private int m_pendingJobs;
    private long m_nextJobId;
    private int m_state;
    private bool m_isStopping;
    private bool m_isExecuting;
    private bool m_isDisposed;
    private bool m_isProbeFailureLogged;
    private Thread? m_thread;

    public PersistenceWorker(
        IGameStore store,
        IOptions<PersistenceOptions> options,
        ServerInstruments instruments,
        ILogger<PersistenceWorker> logger)
    {
        m_store = store;
        m_instruments = instruments;
        m_logger = logger;
        m_capacity = options.Value.QueueCapacity;
        m_timeoutMs = options.Value.CommandTimeoutMs;
        m_maxRetries = options.Value.MaxRetries;
        m_retryBaseDelayMs = options.Value.RetryBaseDelayMs;
    }

    public DatabaseState State => (DatabaseState)Volatile.Read(ref m_state);

    /// <summary>
    ///     Whether admission and durable commands may go to the database now.
    /// </summary>
    public bool IsAvailable => State == DatabaseState.Unknown || State == DatabaseState.Available;

    public int PendingJobs
    {
        get
        {
            lock (m_gate)
            {
                return m_pendingJobs;
            }
        }
    }

    public int WaitingCheckpoints
    {
        get
        {
            lock (m_gate)
            {
                return m_checkpoints.Count;
            }
        }
    }

    public long Retries { get; private set; }

    public void Dispose()
    {
        if (m_isDisposed)
        {
            return;
        }

        Stop(0);
        m_isDisposed = true;
        m_stop.Dispose();
    }

    /// <summary>
    ///     The bound on one retry's wait: up to <c>base × 2^attempt</c>, drawn uniformly ("full jitter") so that
    ///     writers retrying together do not stay in step.
    /// </summary>
    public static int RetryDelayMs(int attempt, int baseDelayMs, Random random)
    {
        long bound = (long)baseDelayMs << Math.Min(attempt, 20);
        return random.Next(0, (int)Math.Min(bound, int.MaxValue - 1) + 1);
    }

    /// <summary>
    ///     Hands <paramref name="job" /> to the writer. Returns false, without queueing it, when the database is not
    ///     available or the queue is full; the caller then refuses the player's request as retryable.
    /// </summary>
    public bool TryEnqueue(PersistenceJob job)
    {
        if (!IsAvailable)
        {
            return false;
        }

        lock (m_gate)
        {
            if (m_isStopping || m_pendingJobs >= m_capacity)
            {
                return false;
            }

            job.Id = ++m_nextJobId;
            m_queue.Enqueue(new Entry(job));
            m_pendingJobs++;
            Monitor.PulseAll(m_gate);
            return true;
        }
    }

    /// <summary>
    ///     Queues <paramref name="checkpoint" /> for its character, replacing one of that character still waiting.
    ///     Checkpoints are accepted during an outage and wait for the database.
    /// </summary>
    public void QueueCheckpoint(PersistenceJob checkpoint)
    {
        checkpoint.IsCheckpoint = true;
        lock (m_gate)
        {
            checkpoint.Id = ++m_nextJobId;
            bool isWaiting = m_checkpoints.ContainsKey(checkpoint.Character);
            m_checkpoints[checkpoint.Character] = checkpoint;
            if (!isWaiting)
            {
                m_queue.Enqueue(Entry.CheckpointOf(checkpoint.Character));
            }

            Monitor.PulseAll(m_gate);
        }
    }

    /// <summary>
    ///     Tick thread: the next finished job, in the order the writer finished them.
    /// </summary>
    public bool TryDequeueCompletion(out PersistenceJob job)
    {
        return m_completions.TryDequeue(out job!);
    }

    /// <summary>
    ///     Asks the database for pending migrations and updates <see cref="State" />. Returns the pending migrations,
    ///     empty when there are none or the database did not answer.
    /// </summary>
    public IReadOnlyList<string> Probe()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(m_stop.Token);
        timeout.CancelAfter(m_timeoutMs);
        try
        {
            IReadOnlyList<string> pending = m_store.GetPendingMigrationsAsync(timeout.Token).GetAwaiter().GetResult();
            if (pending.Count > 0)
            {
                if (SetState(DatabaseState.PendingMigrations))
                {
                    LogMigrationsPending(m_logger, pending.Count, string.Join(", ", pending), null);
                }

                return pending;
            }

            m_isProbeFailureLogged = false;
            if (SetState(DatabaseState.Available))
            {
                LogDatabaseAvailable(m_logger, null);
            }
        }
        catch (Exception exception) when (exception is StoreUnavailableException
                                          || exception is OperationCanceledException)
        {
            if (SetState(DatabaseState.Unavailable))
            {
                LogDatabaseUnavailable(m_logger, exception.InnerException);
            }
        }

        return Array.Empty<string>();
    }

    public void Start()
    {
        m_thread = new Thread(Run)
        {
            Name = "Persistence",
            IsBackground = true
        };
        m_thread.Start();
    }

    /// <summary>
    ///     Stops accepting jobs, lets the writer finish what is queued for at most <paramref name="drainTimeoutMs" />,
    ///     then cancels whatever still runs. Returns whether everything queued was done.
    /// </summary>
    public bool Stop(int drainTimeoutMs)
    {
        var waited = Stopwatch.StartNew();
        lock (m_gate)
        {
            m_isStopping = true;
            Monitor.PulseAll(m_gate);
            if (m_thread != null)
            {
                while ((m_queue.Count > 0 || m_isExecuting) && waited.ElapsedMilliseconds < drainTimeoutMs)
                {
                    Monitor.Wait(m_gate, (int)Math.Min(50L, Math.Max(1L, drainTimeoutMs - waited.ElapsedMilliseconds)));
                }
            }
        }

        if (!m_isDisposed && !m_stop.IsCancellationRequested)
        {
            m_stop.Cancel();
        }

        m_thread?.Join();
        m_thread = null;
        int left;
        lock (m_gate)
        {
            left = m_queue.Count;
        }

        if (left > 0)
        {
            LogDrainIncomplete(m_logger, left, null);
        }

        return left == 0;
    }

    /// <summary>
    ///     Tests: runs the queued work on the calling thread until nothing runnable is left. While the database is
    ///     unavailable it probes once and stops if it still does not answer.
    /// </summary>
    public void RunUntilIdle()
    {
        while (State != DatabaseState.PendingMigrations)
        {
            if (State == DatabaseState.Unavailable)
            {
                ProbeDuringOutage();
                if (State == DatabaseState.Unavailable)
                {
                    return;
                }
            }

            PersistenceJob? job = TakeNext();
            if (job == null)
            {
                lock (m_gate)
                {
                    if (m_queue.Count == 0)
                    {
                        return;
                    }
                }

                continue;
            }

            Execute(job);
        }
    }

    // A database that answers with an error of its own, such as a refused password, stays unavailable. The writer
    // thread must survive it: an exception escaping there would end the process without the shutdown's checkpoints.
    // At startup the same error still stops the server, because the startup check calls Probe itself.
    private void ProbeDuringOutage()
    {
        try
        {
            Probe();
        }
        catch (Exception exception)
        {
            if (!m_isProbeFailureLogged)
            {
                m_isProbeFailureLogged = true;
                LogProbeFailed(m_logger, exception);
            }
        }
    }

    private void Run()
    {
        while (!m_stop.IsCancellationRequested)
        {
            // A schema this build does not know must not be written to; only a restart clears this state.
            if (State == DatabaseState.PendingMigrations)
            {
                m_stop.Token.WaitHandle.WaitOne();
                return;
            }

            if (State == DatabaseState.Unavailable)
            {
                ProbeDuringOutage();
                if (State == DatabaseState.Unavailable)
                {
                    m_stop.Token.WaitHandle.WaitOne(ProbeIntervalMs);
                    continue;
                }
            }

            PersistenceJob? job = TakeNext();
            if (job != null)
            {
                Execute(job);
                continue;
            }

            lock (m_gate)
            {
                Monitor.PulseAll(m_gate);
                if (m_queue.Count == 0)
                {
                    if (m_isStopping)
                    {
                        return;
                    }

                    Monitor.Wait(m_gate);
                }
            }
        }
    }

    // A job leaves the queue and becomes the running one under the same lock, so Stop never sees the moment between
    // the two as a finished drain.
    private PersistenceJob? TakeNext()
    {
        lock (m_gate)
        {
            PersistenceJob? next = Dequeue();
            m_isExecuting = next != null;
            return next;
        }
    }

    private PersistenceJob? Dequeue()
    {
        if (m_queue.Count == 0)
        {
            return null;
        }

        Entry next = m_queue.Peek();
        if (next.Job == null)
        {
            m_queue.Dequeue();
            return m_checkpoints.Remove(next.CheckpointCharacter, out PersistenceJob? checkpoint)
                ? checkpoint
                : null;
        }

        // A job for a character first takes that character's waiting checkpoint; its own turn comes next.
        if (next.Job.Character != 0 && m_checkpoints.Remove(next.Job.Character, out PersistenceJob? earlier))
        {
            return earlier;
        }

        m_queue.Dequeue();
        m_pendingJobs--;
        return next.Job;
    }

    private void Execute(PersistenceJob job)
    {
        try
        {
            Finish(job);
        }
        finally
        {
            lock (m_gate)
            {
                m_isExecuting = false;
                Monitor.PulseAll(m_gate);
            }
        }
    }

    private void Finish(PersistenceJob job)
    {
        var took = Stopwatch.StartNew();
        job.Outcome = Attempt(job);
        m_instruments.RecordJob(job.Operation, job.Outcome, took.Elapsed);
        if (job.Outcome == PersistenceOutcome.Unavailable)
        {
            if (SetState(DatabaseState.Unavailable))
            {
                LogDatabaseUnavailable(m_logger, null);
            }

            if (job.IsCheckpoint)
            {
                Requeue(job);
            }
        }
        else if (job.Outcome == PersistenceOutcome.Succeeded && SetState(DatabaseState.Available))
        {
            LogDatabaseAvailable(m_logger, null);
        }

        m_completions.Enqueue(job);
        if (job.Outcome == PersistenceOutcome.Unavailable && !m_stop.IsCancellationRequested)
        {
            FailQueuedJobs();
        }
    }

    // Work queued before the outage was found would otherwise wait all through it, with nothing said to the player
    // who asked; it ends unavailable now, like work refused at the door (Persistence §9). Checkpoints stay queued for
    // their retry. Not while stopping: that cancellation says nothing about the database.
    private void FailQueuedJobs()
    {
        lock (m_gate)
        {
            int count = m_queue.Count;
            for (int index = 0; index < count; index++)
            {
                Entry entry = m_queue.Dequeue();
                if (entry.Job == null)
                {
                    m_queue.Enqueue(entry);
                    continue;
                }

                m_pendingJobs--;
                entry.Job.Outcome = PersistenceOutcome.Unavailable;
                m_completions.Enqueue(entry.Job);
            }
        }
    }

    private PersistenceOutcome Attempt(PersistenceJob job)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(m_stop.Token);
        timeout.CancelAfter(m_timeoutMs);
        for (int attempt = 0;; attempt++)
        {
            try
            {
                job.ExecuteAsync(m_store, timeout.Token).GetAwaiter().GetResult();
                return PersistenceOutcome.Succeeded;
            }
            catch (StoreUnavailableException) when (attempt < m_maxRetries && !timeout.IsCancellationRequested)
            {
                Retries++;
                m_instruments.RecordRetry(job.Operation);
                bool isCancelled =
                    timeout.Token.WaitHandle.WaitOne(RetryDelayMs(attempt, m_retryBaseDelayMs, m_jitter));
                if (isCancelled)
                {
                    return PersistenceOutcome.Unavailable;
                }
            }
            catch (StoreUnavailableException)
            {
                return PersistenceOutcome.Unavailable;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return PersistenceOutcome.Unavailable;
            }
            catch (Exception exception)
            {
                LogJobFailed(m_logger, job.Operation, job.OperationId, job.Character, job.Connection.Value, exception);
                return PersistenceOutcome.Failed;
            }
        }
    }

    // Back into its slot for a later retry, unless a newer checkpoint of the character is already waiting.
    private void Requeue(PersistenceJob checkpoint)
    {
        lock (m_gate)
        {
            if (m_checkpoints.ContainsKey(checkpoint.Character))
            {
                return;
            }

            m_checkpoints[checkpoint.Character] = checkpoint;
            m_queue.Enqueue(Entry.CheckpointOf(checkpoint.Character));
        }
    }

    // True when the state changed, so each transition is logged once.
    private bool SetState(DatabaseState state)
    {
        return Interlocked.Exchange(ref m_state, (int)state) != (int)state;
    }

    private readonly struct Entry
    {
        public Entry(PersistenceJob job)
        {
            Job = job;
            CheckpointCharacter = 0;
        }

        private Entry(long character)
        {
            Job = null;
            CheckpointCharacter = character;
        }

        public PersistenceJob? Job { get; }

        public long CheckpointCharacter { get; }

        public static Entry CheckpointOf(long character)
        {
            return new Entry(character);
        }
    }
}
}
