namespace Evertorch.Server
{
/// <summary>
///     What the server believes about its database. Only <see cref="Available" /> admits players and accepts durable
///     commands (Persistence §9, System Architecture §10).
/// </summary>
public enum DatabaseState
{
    /// <summary>
    ///     Nothing has been asked yet. Treated as available, so the first job finds out.
    /// </summary>
    Unknown = 0,

    Available = 1,

    /// <summary>
    ///     The last attempt could not reach it or timed out. The writer probes until it answers.
    /// </summary>
    Unavailable = 2,

    /// <summary>
    ///     It answered, but lacks migrations this build needs. Only a restart after migrating clears this.
    /// </summary>
    PendingMigrations = 3
}
}
