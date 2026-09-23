using System;

namespace Evertorch.Persistence
{
/// <summary>
///     The database could not be reached or did not answer in time. Retrying later may succeed; nothing is known to
///     have been written unless the operation says otherwise.
/// </summary>
public sealed class StoreUnavailableException : Exception
{
    public StoreUnavailableException(Exception innerException)
        : base("The database is unavailable.", innerException)
    {
    }
}
}
