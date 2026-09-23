using System;
using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
///     The database lacks migrations this build was made with. The server never applies them itself (Persistence §2).
/// </summary>
public sealed class PendingMigrationsException : Exception
{
    public PendingMigrationsException(IReadOnlyList<string> pending)
        : base($"The database has {pending.Count} pending migration(s): {string.Join(", ", pending)}.")
    {
        Pending = pending;
    }

    public IReadOnlyList<string> Pending { get; }
}
}
