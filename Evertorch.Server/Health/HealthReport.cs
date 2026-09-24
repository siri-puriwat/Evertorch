using System;
using System.Text.Json;

namespace Evertorch.Server
{
/// <summary>
///     What the server's state looks like to the health endpoints at one moment.
/// </summary>
public readonly struct HealthSnapshot
{
    public bool HasFaulted { get; init; }

    public bool IsStopping { get; init; }

    /// <summary>
    ///     How long ago the status was published; null before the first one.
    /// </summary>
    public TimeSpan? StatusAge { get; init; }

    public bool IsAdmissionOpen { get; init; }

    public DatabaseState Database { get; init; }

    /// <summary>
    ///     The test admission itself uses (<see cref="PersistenceWorker.IsAvailable" />).
    /// </summary>
    public bool IsDatabaseAvailable { get; init; }

    public int PendingJobs { get; init; }

    public int QueueCapacity { get; init; }
}

/// <summary>
///     What the health endpoints answer (System Architecture §10), worked out from a snapshot so the rules can be
///     checked without a running server.
/// </summary>
public sealed class HealthReport
{
    private HealthReport(string simulation, string admission, string database, bool isQueueDegraded, bool isReady)
    {
        Simulation = simulation;
        Admission = admission;
        Database = database;
        IsQueueDegraded = isQueueDegraded;
        IsReady = isReady;
    }

    /// <summary>
    ///     <c>starting</c>, <c>ticking</c>, <c>stopping</c>, <c>stalled</c>, or <c>faulted</c>.
    /// </summary>
    public string Simulation { get; }

    public string Admission { get; }

    public string Database { get; }

    public bool IsQueueDegraded { get; }

    /// <summary>
    ///     Not faulted, and ticking, starting, or stopping. A server draining at shutdown is still live.
    /// </summary>
    public bool IsLive => Simulation != "faulted" && Simulation != "stalled";

    /// <summary>
    ///     Ticking, with admission open and the database available. A full server can still be ready.
    /// </summary>
    public bool IsReady { get; }

    public static HealthReport Evaluate(HealthSnapshot snapshot, TimeSpan stallLimit)
    {
        string simulation = snapshot.HasFaulted ? "faulted"
            : snapshot.IsStopping ? "stopping"
            : snapshot.StatusAge == null ? "starting"
            : snapshot.StatusAge > stallLimit ? "stalled"
            : "ticking";
        string database = snapshot.Database switch
        {
            DatabaseState.Available => "available",
            DatabaseState.Unavailable => "unavailable",
            DatabaseState.PendingMigrations => "pending_migrations",
            _ => "unknown"
        };

        // Four fifths of the queue waiting is near capacity: worth a warning, not a reason to refuse players.
        bool isQueueDegraded = snapshot.PendingJobs * 5L >= snapshot.QueueCapacity * 4L;
        bool isReady = simulation == "ticking" && snapshot.IsAdmissionOpen && snapshot.IsDatabaseAvailable;
        return new HealthReport(
            simulation,
            snapshot.IsAdmissionOpen ? "open" : "closed",
            database,
            isQueueDegraded,
            isReady);
    }

    /// <summary>
    ///     Each check and its state, never an exception's text.
    /// </summary>
    public byte[] ToJson()
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                live = IsLive,
                ready = IsReady,
                checks = new
                {
                    simulation = Simulation,
                    admission = Admission,
                    database = Database,
                    persistenceQueue = IsQueueDegraded ? "degraded" : "ok"
                }
            });
    }
}
}
