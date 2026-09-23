namespace Evertorch.Server
{
/// <summary>
///     How a job ended, as the tick thread learns it.
/// </summary>
public enum PersistenceOutcome
{
    Succeeded = 0,

    /// <summary>
    ///     The database could not be reached in time, retries included. Retryable by the player.
    /// </summary>
    Unavailable = 1,

    /// <summary>
    ///     The operation itself failed; retrying it would fail again.
    /// </summary>
    Failed = 2
}
}
