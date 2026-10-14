using System;
using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
///     What a load did with a character's saved status effects (Persistence §7): how many it restored, how many it
///     dropped, and the stored IDs of those the content lacks, which the caller logs.
/// </summary>
public sealed class StatusRestoration
{
    public static readonly StatusRestoration None = new(0, 0, Array.Empty<string>());

    public StatusRestoration(int restored, int dropped, IReadOnlyList<string> unknown)
    {
        Restored = restored;
        Dropped = dropped;
        Unknown = unknown;
    }

    public int Restored { get; }

    /// <summary>
    ///     The converters loaded without a worn weapon and the statuses the content lacks.
    /// </summary>
    public int Dropped { get; }

    public IReadOnlyList<string> Unknown { get; }
}
}
