using System;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Persistence;

namespace Evertorch.Server
{
/// <summary>
///     One unit of database work handed from the tick thread to the writer. <see cref="ExecuteAsync" /> runs on the
///     writer; <see cref="Complete" /> runs later on the tick thread, in the <c>DrainCommands</c> phase, inside the
///     fault boundary of <see cref="Connection" /> (System Architecture §8).
/// </summary>
public abstract class PersistenceJob
{
    protected PersistenceJob(string operation, ConnectionId connection, long character)
    {
        Operation = operation;
        Connection = connection;
        Character = character;
    }

    /// <summary>
    ///     The operation type, for logs.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    ///     The connection whose session a fault in <see cref="Complete" /> closes; default when none.
    /// </summary>
    public ConnectionId Connection { get; }

    /// <summary>
    ///     The character whose state the job reads or writes, or 0. A waiting checkpoint of that character runs first.
    /// </summary>
    public long Character { get; }

    internal PersistenceOutcome Outcome { get; set; }

    /// <summary>
    ///     Set when the job was queued as a checkpoint: it waits in its character's slot and is retried after an
    ///     outage, so its completion can run more than once.
    /// </summary>
    internal bool IsCheckpoint { get; set; }

    public abstract Task ExecuteAsync(IGameStore store, CancellationToken cancellationToken);

    public abstract void Complete();
}

/// <summary>
///     A job built from two delegates: the database work and what the tick thread does with its result.
/// </summary>
public sealed class PersistenceJob<T> : PersistenceJob
{
    private readonly Func<IGameStore, CancellationToken, Task<T>> m_work;
    private readonly Action<PersistenceOutcome, T> m_complete;
    private T m_value = default!;

    public PersistenceJob(
        string operation,
        ConnectionId connection,
        long character,
        Func<IGameStore, CancellationToken, Task<T>> work,
        Action<PersistenceOutcome, T> complete)
        : base(operation, connection, character)
    {
        m_work = work;
        m_complete = complete;
    }

    public override async Task ExecuteAsync(IGameStore store, CancellationToken cancellationToken)
    {
        m_value = await m_work(store, cancellationToken).ConfigureAwait(false);
    }

    public override void Complete()
    {
        m_complete(Outcome, Outcome == PersistenceOutcome.Succeeded ? m_value : default!);
    }
}
}
