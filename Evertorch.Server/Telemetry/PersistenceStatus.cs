namespace Evertorch.Server
{
/// <summary>
///     The persistence bridge as the status publisher last saw it.
/// </summary>
public readonly struct PersistenceStatus
{
    public PersistenceStatus(DatabaseState state, int pendingJobs, int waitingCheckpoints, long retries)
    {
        State = state;
        PendingJobs = pendingJobs;
        WaitingCheckpoints = waitingCheckpoints;
        Retries = retries;
    }

    public DatabaseState State { get; }

    public int PendingJobs { get; }

    public int WaitingCheckpoints { get; }

    public long Retries { get; }
}
}
