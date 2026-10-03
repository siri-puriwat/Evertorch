using System;
using System.Collections.Generic;

namespace Evertorch.Persistence
{
/// <summary>
///     An account's storage as it is now: its revision and every row in row order (Persistence §5).
/// </summary>
public sealed class StoredStorage
{
    public StoredStorage(uint revision, IReadOnlyList<StoredStorageItem> items)
    {
        Revision = revision;
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public uint Revision { get; }

    public IReadOnlyList<StoredStorageItem> Items { get; }
}
}
